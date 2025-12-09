@echo off
cd /d C:\Projects\AAR
set ASPNETCORE_ENVIRONMENT=Development
set ASPNETCORE_URLS=http://localhost:5000
dotnet run --project src\AAR.Api\AAR.Api.csproj --no-launch-profile
pause
