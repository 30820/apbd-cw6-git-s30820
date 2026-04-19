using Microsoft.AspNetCore.Mvc;
using apbd_cw6_git_s30820.DTOs;
using apbd_cw6_git_s30820.Services;

namespace apbd_cw6_git_s30820.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AppointmentsController : ControllerBase
{ 
    private readonly AppointmentService _service;

    public AppointmentsController(AppointmentService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<List<AppointmentListDto>>> GetAppointments(
        [FromQuery] string? status,
        [FromQuery] string? patientLastName)
    {
        var appointments = await _service.GetAppointmentsAsync(status, patientLastName);
        return Ok(appointments);
    }

    [HttpGet("{idAppointment:int}")]
    public async Task<ActionResult<AppointmentDetailsDto>> GetAppointment(int idAppointment)
    {
        var appointment = await _service.GetAppointmentByIdAsync(idAppointment);
        if (appointment is null)
            return NotFound(new ErrorResponseDto { Message = "Appointment not found." });
        return Ok(appointment);
    }

    [HttpPost]
    public async Task<ActionResult> CreateAppointment([FromBody] CreateAppointmentRequestDto dto)
    {
        var (success, createdId, errorMessage, isConflict) = await _service.CreateAppointmentAsync(dto);

        if (!success)
        {
            if (isConflict)
                return Conflict(new ErrorResponseDto { Message = errorMessage! });
            return BadRequest(new ErrorResponseDto { Message = errorMessage! });
        }

        return Created($"/api/appointments/{createdId}", null);
    }

    [HttpPut("{idAppointment:int}")]
    public async Task<ActionResult> UpdateAppointment(int idAppointment,
        [FromBody] UpdateAppointmentRequestDto dto)
    {
        var (success, errorMessage, isNotFound, isConflict) =
            await _service.UpdateAppointmentAsync(idAppointment, dto);

        if (!success)
        {
            if (isNotFound)
                return NotFound(new ErrorResponseDto { Message = errorMessage! });
            if (isConflict)
                return Conflict(new ErrorResponseDto { Message = errorMessage! });
            return BadRequest(new ErrorResponseDto { Message = errorMessage! });
        }

        return Ok();
    }

    [HttpDelete("{idAppointment:int}")]
    public async Task<ActionResult> DeleteAppointment(int idAppointment)
    {
        var (success, errorMessage, isNotFound, isConflict) =
            await _service.DeleteAppointmentAsync(idAppointment);

        if (!success)
        {
            if (isNotFound)
                return NotFound(new ErrorResponseDto { Message = errorMessage! });
            if (isConflict)
                return Conflict(new ErrorResponseDto { Message = errorMessage! });
            return BadRequest(new ErrorResponseDto { Message = errorMessage! });
        }

        return NoContent();
    }
}
