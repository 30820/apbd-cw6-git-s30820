using Microsoft.Data.SqlClient;
using apbd_cw6_git_s30820.DTOs;

namespace apbd_cw6_git_s30820.Services;

public class AppointmentService
{
    private readonly string _connectionString;

    public AppointmentService(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
                            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
    }

    public async Task<List<AppointmentListDto>> GetAppointmentsAsync(string? status, string? patientLastName)
    {
        var results = new List<AppointmentListDto>();

        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand("""
            SELECT
                a.IdAppointment,
                a.AppointmentDate,
                a.Status,
                a.Reason,
                p.FirstName + N' ' + p.LastName AS PatientFullName,
                p.Email AS PatientEmail
            FROM dbo.Appointments a
            JOIN dbo.Patients p ON p.IdPatient = a.IdPatient
            WHERE (@Status IS NULL OR a.Status = @Status)
              AND (@PatientLastName IS NULL OR p.LastName = @PatientLastName)
            ORDER BY a.AppointmentDate;
        """, connection);

        command.Parameters.Add("@Status", System.Data.SqlDbType.NVarChar, 30).Value
            = (object?)status ?? DBNull.Value;
        command.Parameters.Add("@PatientLastName", System.Data.SqlDbType.NVarChar, 80).Value
            = (object?)patientLastName ?? DBNull.Value;

        await connection.OpenAsync();
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            results.Add(new AppointmentListDto
            {
                IdAppointment = reader.GetInt32(0),
                AppointmentDate = reader.GetDateTime(1),
                Status = reader.GetString(2),
                Reason = reader.GetString(3),
                PatientFullName = reader.GetString(4),
                PatientEmail = reader.GetString(5)
            });
        }

        return results;
    }

    public async Task<AppointmentDetailsDto?> GetAppointmentByIdAsync(int idAppointment)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand("""
            SELECT
                a.IdAppointment,
                a.AppointmentDate,
                a.Status,
                a.Reason,
                a.InternalNotes,
                a.CreatedAt,
                p.FirstName + N' ' + p.LastName AS PatientFullName,
                p.Email AS PatientEmail,
                p.PhoneNumber AS PatientPhoneNumber,
                d.FirstName + N' ' + d.LastName AS DoctorFullName,
                d.LicenseNumber AS DoctorLicenseNumber,
                s.Name AS SpecializationName
            FROM dbo.Appointments a
            JOIN dbo.Patients p ON p.IdPatient = a.IdPatient
            JOIN dbo.Doctors d ON d.IdDoctor = a.IdDoctor
            JOIN dbo.Specializations s ON s.IdSpecialization = d.IdSpecialization
            WHERE a.IdAppointment = @IdAppointment;
        """, connection);

        command.Parameters.Add("@IdAppointment", System.Data.SqlDbType.Int).Value = idAppointment;

        await connection.OpenAsync();
        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
            return null;

        return new AppointmentDetailsDto
        {
            IdAppointment = reader.GetInt32(0),
            AppointmentDate = reader.GetDateTime(1),
            Status = reader.GetString(2),
            Reason = reader.GetString(3),
            InternalNotes = reader.IsDBNull(4) ? null : reader.GetString(4),
            CreatedAt = reader.GetDateTime(5),
            PatientFullName = reader.GetString(6),
            PatientEmail = reader.GetString(7),
            PatientPhoneNumber = reader.GetString(8),
            DoctorFullName = reader.GetString(9),
            DoctorLicenseNumber = reader.GetString(10),
            SpecializationName = reader.GetString(11)
        };
    }

    public async Task<(bool Success, int? CreatedId, string? ErrorMessage, bool IsConflict)> CreateAppointmentAsync(
        CreateAppointmentRequestDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Reason) || dto.Reason.Length > 250)
            return (false, null, "Reason is required and must be at most 250 characters.", false);

        if (dto.AppointmentDate <= DateTime.UtcNow)
            return (false, null, "Appointment date must be in the future.", false);

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        // Check patient exists and is active
        await using (var cmd = new SqlCommand(
                         "SELECT IsActive FROM dbo.Patients WHERE IdPatient = @IdPatient;", connection))
        {
            cmd.Parameters.Add("@IdPatient", System.Data.SqlDbType.Int).Value = dto.IdPatient;
            var result = await cmd.ExecuteScalarAsync();
            if (result is null)
                return (false, null, "Patient not found.", false);
            if (!(bool)result)
                return (false, null, "Patient is not active.", false);
        }

        // Check doctor exists and is active
        await using (var cmd = new SqlCommand(
                         "SELECT IsActive FROM dbo.Doctors WHERE IdDoctor = @IdDoctor;", connection))
        {
            cmd.Parameters.Add("@IdDoctor", System.Data.SqlDbType.Int).Value = dto.IdDoctor;
            var result = await cmd.ExecuteScalarAsync();
            if (result is null)
                return (false, null, "Doctor not found.", false);
            if (!(bool)result)
                return (false, null, "Doctor is not active.", false);
        }

        // Check schedule conflict
        await using (var cmd = new SqlCommand("""
            SELECT COUNT(1) FROM dbo.Appointments
            WHERE IdDoctor = @IdDoctor
              AND AppointmentDate = @AppointmentDate
              AND Status = N'Scheduled';
        """, connection))
        {
            cmd.Parameters.Add("@IdDoctor", System.Data.SqlDbType.Int).Value = dto.IdDoctor;
            cmd.Parameters.Add("@AppointmentDate", System.Data.SqlDbType.DateTime2).Value = dto.AppointmentDate;
            var count = (int)(await cmd.ExecuteScalarAsync())!;
            if (count > 0)
                return (false, null, "Doctor already has a scheduled appointment at this time.", true);
        }

        // Insert
        await using (var cmd = new SqlCommand("""
            INSERT INTO dbo.Appointments (IdPatient, IdDoctor, AppointmentDate, Status, Reason)
            VALUES (@IdPatient, @IdDoctor, @AppointmentDate, N'Scheduled', @Reason);
            SELECT CAST(SCOPE_IDENTITY() AS INT);
        """, connection))
        {
            cmd.Parameters.Add("@IdPatient", System.Data.SqlDbType.Int).Value = dto.IdPatient;
            cmd.Parameters.Add("@IdDoctor", System.Data.SqlDbType.Int).Value = dto.IdDoctor;
            cmd.Parameters.Add("@AppointmentDate", System.Data.SqlDbType.DateTime2).Value = dto.AppointmentDate;
            cmd.Parameters.Add("@Reason", System.Data.SqlDbType.NVarChar, 250).Value = dto.Reason;

            var createdId = (int)(await cmd.ExecuteScalarAsync())!;
            return (true, createdId, null, false);
        }
    }

    public async Task<(bool Success, string? ErrorMessage, bool IsNotFound, bool IsConflict)> UpdateAppointmentAsync(
        int idAppointment, UpdateAppointmentRequestDto dto)
    {
        var validStatuses = new[] { "Scheduled", "Completed", "Cancelled" };
        if (!validStatuses.Contains(dto.Status))
            return (false, "Invalid status. Must be: Scheduled, Completed, or Cancelled.", false, false);

        if (string.IsNullOrWhiteSpace(dto.Reason) || dto.Reason.Length > 250)
            return (false, "Reason is required and must be at most 250 characters.", false, false);

        if (dto.AppointmentDate <= DateTime.UtcNow)
            return (false, "Appointment date must be in the future.", false, false);

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        // Check appointment exists and get current status/date
        string? currentStatus;
        DateTime currentDate;

        await using (var cmd = new SqlCommand(
                         "SELECT Status, AppointmentDate FROM dbo.Appointments WHERE IdAppointment = @Id;",
                         connection))
        {
            cmd.Parameters.Add("@Id", System.Data.SqlDbType.Int).Value = idAppointment;
            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return (false, "Appointment not found.", true, false);
            currentStatus = reader.GetString(0);
            currentDate = reader.GetDateTime(1);
        }

        // If current status is Completed, do not allow date change
        if (currentStatus == "Completed" && dto.AppointmentDate != currentDate)
            return (false, "Cannot change the date of a completed appointment.", false, true);

        // Check patient exists and is active
        await using (var cmd = new SqlCommand(
                         "SELECT IsActive FROM dbo.Patients WHERE IdPatient = @IdPatient;", connection))
        {
            cmd.Parameters.Add("@IdPatient", System.Data.SqlDbType.Int).Value = dto.IdPatient;
            var result = await cmd.ExecuteScalarAsync();
            if (result is null)
                return (false, "Patient not found.", false, false);
            if (!(bool)result)
                return (false, "Patient is not active.", false, false);
        }

        // Check doctor exists and is active
        await using (var cmd = new SqlCommand(
                         "SELECT IsActive FROM dbo.Doctors WHERE IdDoctor = @IdDoctor;", connection))
        {
            cmd.Parameters.Add("@IdDoctor", System.Data.SqlDbType.Int).Value = dto.IdDoctor;
            var result = await cmd.ExecuteScalarAsync();
            if (result is null)
                return (false, "Doctor not found.", false, false);
            if (!(bool)result)
                return (false, "Doctor is not active.", false, false);
        }

        // Check schedule conflict (exclude current appointment)
        await using (var cmd = new SqlCommand("""
            SELECT COUNT(1) FROM dbo.Appointments
            WHERE IdDoctor = @IdDoctor
              AND AppointmentDate = @AppointmentDate
              AND Status = N'Scheduled'
              AND IdAppointment <> @IdAppointment;
        """, connection))
        {
            cmd.Parameters.Add("@IdDoctor", System.Data.SqlDbType.Int).Value = dto.IdDoctor;
            cmd.Parameters.Add("@AppointmentDate", System.Data.SqlDbType.DateTime2).Value = dto.AppointmentDate;
            cmd.Parameters.Add("@IdAppointment", System.Data.SqlDbType.Int).Value = idAppointment;
            var count = (int)(await cmd.ExecuteScalarAsync())!;
            if (count > 0)
                return (false, "Doctor already has a scheduled appointment at this time.", false, true);
        }

        // Update
        await using (var cmd = new SqlCommand("""
            UPDATE dbo.Appointments
            SET IdPatient = @IdPatient,
                IdDoctor = @IdDoctor,
                AppointmentDate = @AppointmentDate,
                Status = @Status,
                Reason = @Reason,
                InternalNotes = @InternalNotes
            WHERE IdAppointment = @IdAppointment;
        """, connection))
        {
            cmd.Parameters.Add("@IdPatient", System.Data.SqlDbType.Int).Value = dto.IdPatient;
            cmd.Parameters.Add("@IdDoctor", System.Data.SqlDbType.Int).Value = dto.IdDoctor;
            cmd.Parameters.Add("@AppointmentDate", System.Data.SqlDbType.DateTime2).Value = dto.AppointmentDate;
            cmd.Parameters.Add("@Status", System.Data.SqlDbType.NVarChar, 30).Value = dto.Status;
            cmd.Parameters.Add("@Reason", System.Data.SqlDbType.NVarChar, 250).Value = dto.Reason;
            cmd.Parameters.Add("@InternalNotes", System.Data.SqlDbType.NVarChar, 500).Value
                = (object?)dto.InternalNotes ?? DBNull.Value;
            cmd.Parameters.Add("@IdAppointment", System.Data.SqlDbType.Int).Value = idAppointment;

            await cmd.ExecuteNonQueryAsync();
        }

        return (true, null, false, false);
    }

    public async Task<(bool Success, string? ErrorMessage, bool IsNotFound, bool IsConflict)> DeleteAppointmentAsync(
        int idAppointment)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        // Check exists and get status
        await using (var cmd = new SqlCommand(
                         "SELECT Status FROM dbo.Appointments WHERE IdAppointment = @Id;", connection))
        {
            cmd.Parameters.Add("@Id", System.Data.SqlDbType.Int).Value = idAppointment;
            var result = await cmd.ExecuteScalarAsync();
            if (result is null)
                return (false, "Appointment not found.", true, false);
            if ((string)result == "Completed")
                return (false, "Cannot delete a completed appointment.", false, true);
        }

        // Delete
        await using (var cmd = new SqlCommand(
                         "DELETE FROM dbo.Appointments WHERE IdAppointment = @Id;", connection))
        {
            cmd.Parameters.Add("@Id", System.Data.SqlDbType.Int).Value = idAppointment;
            await cmd.ExecuteNonQueryAsync();
        }

        return (true, null, false, false);
    }
}
