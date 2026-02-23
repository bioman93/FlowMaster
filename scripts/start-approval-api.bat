@echo off
chcp 65001 >nul
echo ============================================
echo  ApprovalService API 서버 시작 (개발 환경)
echo  URL: http://localhost:5002
echo  Swagger: http://localhost:5002/swagger
echo  (VehicleReservation이 5001을 사용하므로 5002 사용)
echo ============================================
echo.

set API_PATH=C:\Works\DevSuite\ApprovalSystem\src\ApprovalService\ApprovalService.API

if not exist "%API_PATH%\ApprovalService.API.csproj" (
    echo [오류] ApprovalService.API 프로젝트를 찾을 수 없습니다.
    echo 경로: %API_PATH%
    pause
    exit /b 1
)

echo API 서버를 시작합니다...
echo 종료하려면 Ctrl+C를 누르세요.
echo.

cd /d "%API_PATH%"
dotnet run --urls "http://localhost:5002" --environment Development
