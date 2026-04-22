using ClinicAdoNetApi.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace ClinicAdoNetApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AppointmentsController : ControllerBase
{
    private readonly IConfiguration _configuration;

    public AppointmentsController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    private string GetConnectionString()
    {
        return _configuration.GetConnectionString("DefaultConnection")
               ?? throw new InvalidOperationException("Missing connection string.");
    }

    [HttpGet]
    public async Task<IActionResult> GetAppointments([FromQuery] string? status, [FromQuery] string? patientLastName)
    {
        var appointments = new List<AppointmentListDto>();

        var connectionString = GetConnectionString();

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        var sql = @"
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
            ORDER BY a.AppointmentDate;";

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Status", (object?)status ?? DBNull.Value);
        command.Parameters.AddWithValue("@PatientLastName", (object?)patientLastName ?? DBNull.Value);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            appointments.Add(new AppointmentListDto
            {
                IdAppointment = reader.GetInt32(reader.GetOrdinal("IdAppointment")),
                AppointmentDate = reader.GetDateTime(reader.GetOrdinal("AppointmentDate")),
                Status = reader.GetString(reader.GetOrdinal("Status")),
                Reason = reader.GetString(reader.GetOrdinal("Reason")),
                PatientFullName = reader.GetString(reader.GetOrdinal("PatientFullName")),
                PatientEmail = reader.GetString(reader.GetOrdinal("PatientEmail"))
            });
        }

        return Ok(appointments);
    }

    [HttpGet("{idAppointment:int}")]
    public async Task<IActionResult> GetAppointmentById(int idAppointment)
    {
        var connectionString = GetConnectionString();

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        var sql = @"
            SELECT
                a.IdAppointment,
                a.AppointmentDate,
                a.Status,
                a.Reason,
                a.InternalNotes,
                a.CreatedAt,
                p.IdPatient,
                p.FirstName AS PatientFirstName,
                p.LastName AS PatientLastName,
                p.Email,
                p.PhoneNumber,
                p.DateOfBirth,
                d.IdDoctor,
                d.FirstName AS DoctorFirstName,
                d.LastName AS DoctorLastName,
                d.LicenseNumber,
                s.Name AS SpecializationName
            FROM dbo.Appointments a
            JOIN dbo.Patients p ON p.IdPatient = a.IdPatient
            JOIN dbo.Doctors d ON d.IdDoctor = a.IdDoctor
            JOIN dbo.Specializations s ON s.IdSpecialization = d.IdSpecialization
            WHERE a.IdAppointment = @IdAppointment;";

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@IdAppointment", idAppointment);

        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return NotFound(new ErrorResponseDto
            {
                Message = $"Appointment with id {idAppointment} was not found."
            });
        }

        var dto = new AppointmentDetailsDto
        {
            IdAppointment = reader.GetInt32(reader.GetOrdinal("IdAppointment")),
            AppointmentDate = reader.GetDateTime(reader.GetOrdinal("AppointmentDate")),
            Status = reader.GetString(reader.GetOrdinal("Status")),
            Reason = reader.GetString(reader.GetOrdinal("Reason")),
            InternalNotes = reader.IsDBNull(reader.GetOrdinal("InternalNotes"))
                ? null
                : reader.GetString(reader.GetOrdinal("InternalNotes")),
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),

            IdPatient = reader.GetInt32(reader.GetOrdinal("IdPatient")),
            PatientFirstName = reader.GetString(reader.GetOrdinal("PatientFirstName")),
            PatientLastName = reader.GetString(reader.GetOrdinal("PatientLastName")),
            PatientEmail = reader.GetString(reader.GetOrdinal("Email")),
            PatientPhoneNumber = reader.GetString(reader.GetOrdinal("PhoneNumber")),
            DateOfBirth = reader.GetDateTime(reader.GetOrdinal("DateOfBirth")),

            IdDoctor = reader.GetInt32(reader.GetOrdinal("IdDoctor")),
            DoctorFirstName = reader.GetString(reader.GetOrdinal("DoctorFirstName")),
            DoctorLastName = reader.GetString(reader.GetOrdinal("DoctorLastName")),
            LicenseNumber = reader.GetString(reader.GetOrdinal("LicenseNumber")),
            SpecializationName = reader.GetString(reader.GetOrdinal("SpecializationName"))
        };

        return Ok(dto);
    }

    [HttpPost]
    public async Task<IActionResult> CreateAppointment([FromBody] CreateAppointmentRequestDto request)
    {
        if (request.IdPatient <= 0 || request.IdDoctor <= 0)
        {
            return BadRequest(new ErrorResponseDto
            {
                Message = "IdPatient and IdDoctor must be greater than 0."
            });
        }

        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 250)
        {
            return BadRequest(new ErrorResponseDto
            {
                Message = "Reason is required and must be at most 250 characters."
            });
        }

        if (request.AppointmentDate <= DateTime.Now)
        {
            return BadRequest(new ErrorResponseDto
            {
                Message = "Appointment date cannot be in the past."
            });
        }

        var connectionString = GetConnectionString();

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        var patientExistsSql = @"
            SELECT COUNT(1)
            FROM dbo.Patients
            WHERE IdPatient = @IdPatient AND IsActive = 1;";

        await using (var patientCommand = new SqlCommand(patientExistsSql, connection))
        {
            patientCommand.Parameters.AddWithValue("@IdPatient", request.IdPatient);
            var patientCount = (int)await patientCommand.ExecuteScalarAsync();

            if (patientCount == 0)
            {
                return BadRequest(new ErrorResponseDto
                {
                    Message = "Patient does not exist or is inactive."
                });
            }
        }

        var doctorExistsSql = @"
            SELECT COUNT(1)
            FROM dbo.Doctors
            WHERE IdDoctor = @IdDoctor AND IsActive = 1;";

        await using (var doctorCommand = new SqlCommand(doctorExistsSql, connection))
        {
            doctorCommand.Parameters.AddWithValue("@IdDoctor", request.IdDoctor);
            var doctorCount = (int)await doctorCommand.ExecuteScalarAsync();

            if (doctorCount == 0)
            {
                return BadRequest(new ErrorResponseDto
                {
                    Message = "Doctor does not exist or is inactive."
                });
            }
        }

        var conflictSql = @"
            SELECT COUNT(1)
            FROM dbo.Appointments
            WHERE IdDoctor = @IdDoctor
              AND AppointmentDate = @AppointmentDate
              AND Status = N'Scheduled';";

        await using (var conflictCommand = new SqlCommand(conflictSql, connection))
        {
            conflictCommand.Parameters.AddWithValue("@IdDoctor", request.IdDoctor);
            conflictCommand.Parameters.AddWithValue("@AppointmentDate", request.AppointmentDate);

            var conflictCount = (int)await conflictCommand.ExecuteScalarAsync();

            if (conflictCount > 0)
            {
                return Conflict(new ErrorResponseDto
                {
                    Message = "Doctor already has an appointment at this time."
                });
            }
        }

        var insertSql = @"
            INSERT INTO dbo.Appointments (IdPatient, IdDoctor, AppointmentDate, Status, Reason)
            OUTPUT INSERTED.IdAppointment
            VALUES (@IdPatient, @IdDoctor, @AppointmentDate, @Status, @Reason);";

        int newId;

        await using (var insertCommand = new SqlCommand(insertSql, connection))
        {
            insertCommand.Parameters.AddWithValue("@IdPatient", request.IdPatient);
            insertCommand.Parameters.AddWithValue("@IdDoctor", request.IdDoctor);
            insertCommand.Parameters.AddWithValue("@AppointmentDate", request.AppointmentDate);
            insertCommand.Parameters.AddWithValue("@Status", "Scheduled");
            insertCommand.Parameters.AddWithValue("@Reason", request.Reason);

            newId = (int)await insertCommand.ExecuteScalarAsync();
        }

        return CreatedAtAction(nameof(GetAppointmentById), new { idAppointment = newId }, new { idAppointment = newId });
    }

    [HttpPut("{idAppointment:int}")]
    public async Task<IActionResult> UpdateAppointment(int idAppointment, [FromBody] UpdateAppointmentRequestDto request)
    {
        var allowedStatuses = new[] { "Scheduled", "Completed", "Cancelled" };

        if (!allowedStatuses.Contains(request.Status))
        {
            return BadRequest(new ErrorResponseDto
            {
                Message = "Status must be one of: Scheduled, Completed, Cancelled."
            });
        }

        if (request.IdPatient <= 0 || request.IdDoctor <= 0)
        {
            return BadRequest(new ErrorResponseDto
            {
                Message = "IdPatient and IdDoctor must be greater than 0."
            });
        }

        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 250)
        {
            return BadRequest(new ErrorResponseDto
            {
                Message = "Reason is required and must be at most 250 characters."
            });
        }

        var connectionString = GetConnectionString();

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        string currentStatus;
        DateTime currentAppointmentDate;

        var existingSql = @"
            SELECT Status, AppointmentDate
            FROM dbo.Appointments
            WHERE IdAppointment = @IdAppointment;";

        await using (var existingCommand = new SqlCommand(existingSql, connection))
        {
            existingCommand.Parameters.AddWithValue("@IdAppointment", idAppointment);

            await using var reader = await existingCommand.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
            {
                return NotFound(new ErrorResponseDto
                {
                    Message = "Appointment not found."
                });
            }

            currentStatus = reader.GetString(reader.GetOrdinal("Status"));
            currentAppointmentDate = reader.GetDateTime(reader.GetOrdinal("AppointmentDate"));
        }

        if (currentStatus == "Completed" && request.AppointmentDate != currentAppointmentDate)
        {
            return Conflict(new ErrorResponseDto
            {
                Message = "Completed appointment date cannot be changed."
            });
        }

        var patientSql = @"
            SELECT COUNT(1)
            FROM dbo.Patients
            WHERE IdPatient = @IdPatient AND IsActive = 1;";

        await using (var patientCommand = new SqlCommand(patientSql, connection))
        {
            patientCommand.Parameters.AddWithValue("@IdPatient", request.IdPatient);
            var patientCount = (int)await patientCommand.ExecuteScalarAsync();

            if (patientCount == 0)
            {
                return BadRequest(new ErrorResponseDto
                {
                    Message = "Patient does not exist or is inactive."
                });
            }
        }

        var doctorSql = @"
            SELECT COUNT(1)
            FROM dbo.Doctors
            WHERE IdDoctor = @IdDoctor AND IsActive = 1;";

        await using (var doctorCommand = new SqlCommand(doctorSql, connection))
        {
            doctorCommand.Parameters.AddWithValue("@IdDoctor", request.IdDoctor);
            var doctorCount = (int)await doctorCommand.ExecuteScalarAsync();

            if (doctorCount == 0)
            {
                return BadRequest(new ErrorResponseDto
                {
                    Message = "Doctor does not exist or is inactive."
                });
            }
        }

        if (request.AppointmentDate != currentAppointmentDate)
        {
            var conflictSql = @"
                SELECT COUNT(1)
                FROM dbo.Appointments
                WHERE IdDoctor = @IdDoctor
                  AND AppointmentDate = @AppointmentDate
                  AND IdAppointment <> @IdAppointment
                  AND Status = N'Scheduled';";

            await using var conflictCommand = new SqlCommand(conflictSql, connection);
            conflictCommand.Parameters.AddWithValue("@IdDoctor", request.IdDoctor);
            conflictCommand.Parameters.AddWithValue("@AppointmentDate", request.AppointmentDate);
            conflictCommand.Parameters.AddWithValue("@IdAppointment", idAppointment);

            var conflictCount = (int)await conflictCommand.ExecuteScalarAsync();

            if (conflictCount > 0)
            {
                return Conflict(new ErrorResponseDto
                {
                    Message = "Doctor already has an appointment at this time."
                });
            }
        }

        var updateSql = @"
            UPDATE dbo.Appointments
            SET IdPatient = @IdPatient,
                IdDoctor = @IdDoctor,
                AppointmentDate = @AppointmentDate,
                Status = @Status,
                Reason = @Reason,
                InternalNotes = @InternalNotes
            WHERE IdAppointment = @IdAppointment;";

        await using var updateCommand = new SqlCommand(updateSql, connection);
        updateCommand.Parameters.AddWithValue("@IdPatient", request.IdPatient);
        updateCommand.Parameters.AddWithValue("@IdDoctor", request.IdDoctor);
        updateCommand.Parameters.AddWithValue("@AppointmentDate", request.AppointmentDate);
        updateCommand.Parameters.AddWithValue("@Status", request.Status);
        updateCommand.Parameters.AddWithValue("@Reason", request.Reason);
        updateCommand.Parameters.AddWithValue("@InternalNotes", (object?)request.InternalNotes ?? DBNull.Value);
        updateCommand.Parameters.AddWithValue("@IdAppointment", idAppointment);

        await updateCommand.ExecuteNonQueryAsync();

        return Ok(new { message = "Appointment updated." });
    }

    [HttpDelete("{idAppointment:int}")]
    public async Task<IActionResult> DeleteAppointment(int idAppointment)
    {
        var connectionString = GetConnectionString();

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        var selectSql = @"
            SELECT Status
            FROM dbo.Appointments
            WHERE IdAppointment = @IdAppointment;";

        string status;

        await using (var selectCommand = new SqlCommand(selectSql, connection))
        {
            selectCommand.Parameters.AddWithValue("@IdAppointment", idAppointment);

            var result = await selectCommand.ExecuteScalarAsync();

            if (result == null)
            {
                return NotFound(new ErrorResponseDto
                {
                    Message = "Appointment not found."
                });
            }

            status = result.ToString()!;
        }

        if (status == "Completed")
        {
            return Conflict(new ErrorResponseDto
            {
                Message = "Completed appointment cannot be deleted."
            });
        }

        var deleteSql = @"
            DELETE FROM dbo.Appointments
            WHERE IdAppointment = @IdAppointment;";

        await using var deleteCommand = new SqlCommand(deleteSql, connection);
        deleteCommand.Parameters.AddWithValue("@IdAppointment", idAppointment);

        await deleteCommand.ExecuteNonQueryAsync();

        return NoContent();
    }
}