# ClinicAdoNetApi

ASP.NET Core Web API z ADO.NET i SQL Server.

## Endpointy
- GET /api/Appointments
- GET /api/Appointments/{idAppointment}
- POST /api/Appointments
- PUT /api/Appointments/{idAppointment}
- DELETE /api/Appointments/{idAppointment}

## Technologie
- ASP.NET Core Web API
- Microsoft.Data.SqlClient
- SQL Server
- ADO.NET

## Uruchomienie
1. Uruchomić skrypt `01_create_and_seed_clinic.sql`
2. Ustawić connection string w `appsettings.json`
3. Uruchomić projekt
4. Testować w Swaggerze