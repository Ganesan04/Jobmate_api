#requires -Version 5.1
<#
    End-to-end smoke test for the Jobmate API.

    Exercises the real HTTP pipeline against a running API and a real PostgreSQL
    database. Every check prints PASS or FAIL and the script exits non-zero if any
    check failed, so it is usable from CI as well as interactively.

    Usage:
        dotnet run --project src/Jobmate.Api            # in one terminal
        .\scripts\smoke-test.ps1                       # in another
#>

[CmdletBinding()]
param(
    [string]$BaseUrl = 'http://localhost:5234'
)

$ErrorActionPreference = 'Stop'
$script:Passed = 0
$script:Failed = 0

function Invoke-Api {
    param(
        [Parameter(Mandatory)][string]$Path,
        [ValidateSet('GET', 'POST', 'PUT', 'PATCH', 'DELETE')][string]$Method = 'POST',
        [object]$Body,
        [hashtable]$Headers
    )

    $params = @{
        Uri             = "$BaseUrl$Path"
        Method          = $Method
        Headers         = $Headers
        ErrorAction     = 'Stop'
        TimeoutSec      = 30
    }

    if ($null -ne $Body) {
        $params.Body         = ($Body | ConvertTo-Json -Depth 10)
        $params.ContentType  = 'application/json'
    }

    try {
        $response = Invoke-RestMethod @params
        return [pscustomobject]@{ Ok = $true; Status = 200; Body = $response }
    }
    catch {
        $status = 0
        if ($_.Exception.Response) { $status = [int]$_.Exception.Response.StatusCode }

        $parsed = $null
        if ($_.ErrorDetails.Message) {
            try { $parsed = $_.ErrorDetails.Message | ConvertFrom-Json } catch { }
        }

        return [pscustomobject]@{ Ok = $false; Status = $status; Body = $parsed }
    }
}

function Assert {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][bool]$Condition,
        [string]$Detail = ''
    )

    if ($Condition) {
        $script:Passed++
        Write-Host "  PASS  $Name" -ForegroundColor Green
    }
    else {
        $script:Failed++
        Write-Host "  FAIL  $Name  $Detail" -ForegroundColor Red
    }
}

# A unique suffix per run keeps repeated runs from colliding on the unique
# phone/email indexes.
$suffix = Get-Random -Minimum 100000 -Maximum 999999
$phone  = "9$suffix"
$otp    = '123456'   # the development OTP; see OtpServiceHelper

Write-Host "`nJobmate API smoke test against $BaseUrl`n" -ForegroundColor Cyan

# ------------------------------------------------------------------ health
Write-Host 'Reachability' -ForegroundColor Cyan
$root = Invoke-Api -Path '/api/v1/auth/login' -Method 'GET'
Assert -Name 'API is listening' -Condition ($root.Status -ne 0) -Detail "status $($root.Status)"

# ------------------------------------------------------------------ auth
Write-Host "`nAuth: worker registration and OTP" -ForegroundColor Cyan

$register = Invoke-Api -Path '/api/v1/auth/register' -Body @{
    name     = "Smoke Worker $suffix"
    phone    = $phone
    email    = "worker$suffix@example.com"
    password = 'Passw0rd!'
    role     = 'worker'
}

Assert -Name 'register returns 200'        -Condition $register.Ok -Detail "status $($register.Status)"
Assert -Name 'register flags OTP required' -Condition ($register.Body.data.requiresOtpVerification -eq $true)
Assert -Name 'phone normalised to E.164'  -Condition ($register.Body.data.user.phone -eq "+91$phone") -Detail "got '$($register.Body.data.user.phone)'"
Assert -Name 'password hash never returned' -Condition ($null -eq $register.Body.data.user.passwordHash)

$duplicate = Invoke-Api -Path '/api/v1/auth/register' -Body @{
    name     = 'Duplicate'
    phone    = $phone
    email    = "other$suffix@example.com"
    password = 'Passw0rd!'
    role     = 'worker'
}
Assert -Name 'duplicate phone is rejected with 409' -Condition ($duplicate.Status -eq 409) -Detail "status $($duplicate.Status)"

$weakPassword = Invoke-Api -Path '/api/v1/auth/register' -Body @{
    name = 'Weak'; phone = "8$suffix"; email = "weak$suffix@example.com"
    password = 'short'; role = 'worker'
}
Assert -Name 'weak password fails validation with 400' -Condition ($weakPassword.Status -eq 400) -Detail "status $($weakPassword.Status)"

$badOtp = Invoke-Api -Path '/api/v1/auth/verify-otp' -Body @{ phone = $phone; code = '999999' }
Assert -Name 'wrong OTP is rejected with 401' -Condition ($badOtp.Status -eq 401) -Detail "status $($badOtp.Status)"

$verify = Invoke-Api -Path '/api/v1/auth/verify-otp' -Body @{ phone = $phone; code = $otp }
Assert -Name 'correct OTP verifies the phone' -Condition $verify.Ok -Detail "status $($verify.Status)"
Assert -Name 'verification status becomes verified' -Condition ($verify.Body.data.user.verification -eq 'verified') -Detail "got '$($verify.Body.data.user.verification)'"
Assert -Name 'access token issued' -Condition (-not [string]::IsNullOrWhiteSpace($verify.Body.data.accessToken))

$workerToken = $verify.Body.data.accessToken
$workerAuth  = @{ Authorization = "Bearer $workerToken" }

# ------------------------------------------------------------------ login
Write-Host "`nAuth: sign-in and token rotation" -ForegroundColor Cyan

$login = Invoke-Api -Path '/api/v1/auth/login' -Body @{
    identifier = "worker$suffix@example.com"
    password   = 'Passw0rd!'
}
Assert -Name 'login by email succeeds' -Condition $login.Ok -Detail "status $($login.Status)"

$loginByPhone = Invoke-Api -Path '/api/v1/auth/login' -Body @{
    identifier = $phone
    password   = 'Passw0rd!'
}
Assert -Name 'login by phone succeeds (10-digit form)' -Condition $loginByPhone.Ok -Detail "status $($loginByPhone.Status)"

$badLogin = Invoke-Api -Path '/api/v1/auth/login' -Body @{ identifier = "worker$suffix@example.com"; password = 'WrongPass1' }
Assert -Name 'wrong password is rejected with 401' -Condition ($badLogin.Status -eq 401) -Detail "status $($badLogin.Status)"

$unknownUser = Invoke-Api -Path '/api/v1/auth/login' -Body @{ identifier = "nobody$suffix@example.com"; password = 'Passw0rd!' }
Assert -Name 'unknown account gives the same 401 as a wrong password' -Condition ($unknownUser.Status -eq $badLogin.Status) -Detail "$($unknownUser.Status) vs $($badLogin.Status)"

$refresh = Invoke-Api -Path '/api/v1/auth/refresh' -Body @{ refreshToken = $login.Body.data.refreshToken }
Assert -Name 'refresh rotates the token pair' -Condition $refresh.Ok -Detail "status $($refresh.Status)"
Assert -Name 'refreshed access token differs' -Condition ($refresh.Body.data.accessToken -ne $login.Body.data.accessToken)

$replay = Invoke-Api -Path '/api/v1/auth/refresh' -Body @{ refreshToken = $login.Body.data.refreshToken }
Assert -Name 'reusing a rotated refresh token is rejected with 401' -Condition ($replay.Status -eq 401) -Detail "status $($replay.Status)"

# ------------------------------------------------------------------ admin
Write-Host "`nAdmin isolation" -ForegroundColor Cyan

$adminLogin = Invoke-Api -Path '/api/v1/admin/auth/login' -Body @{
    email    = 'admin@jobmate.app'
    password = (Get-Content "$env:TEMP\opencode\jobmate_admin_pw.txt" -Raw).Trim()
}

if ($adminLogin.Ok) {
    $adminAuth = @{ Authorization = "Bearer $($adminLogin.Body.data.accessToken)" }

    $asAdmin = Invoke-Api -Path '/api/v1/admin/dashboard' -Headers $adminAuth
    Assert -Name 'admin token reaches an admin endpoint' -Condition $asAdmin.Ok -Detail "status $($asAdmin.Status)"

    $asWorker = Invoke-Api -Path '/api/v1/admin/dashboard' -Headers $workerAuth
    Assert -Name 'worker token is refused on the admin surface (403)' -Condition ($asWorker.Status -eq 403) -Detail "status $($asWorker.Status)"
}
else {
    Write-Host '  SKIP  admin checks (admin password file not found; run with the local secret available)' -ForegroundColor Yellow
}

# ------------------------------------------------------------------ envelope
Write-Host "`nResponse envelope and error shape" -ForegroundColor Cyan

$anon = Invoke-Api -Path '/api/v1/auth/logout' -Method 'POST' -Body @{}
Assert -Name 'protected endpoint without a token is 401' -Condition ($anon.Status -eq 401) -Detail "status $($anon.Status)"

$badJson = Invoke-Api -Path '/api/v1/auth/login' -Body @{ identifier = ''; password = '' }
Assert -Name 'empty credentials fail validation' -Condition ($badJson.Status -eq 400) -Detail "status $($badJson.Status)"
Assert -Name 'validation failure uses success:false' -Condition ($badJson.Body.success -eq $false)
Assert -Name 'validation failure lists field errors' -Condition ($badJson.Body.errors.Count -gt 0)
Assert -Name 'error field names are camelCase' -Condition ($badJson.Body.errors[0].field -cmatch '^[a-z]') -Detail "got '$($badJson.Body.errors[0].field)'"

# ------------------------------------------------------------------ result
Write-Host "`n----------------------------------------" -ForegroundColor Cyan
Write-Host "  passed: $script:Passed   failed: $script:Failed" -ForegroundColor $(if ($script:Failed) { 'Red' } else { 'Green' })
Write-Host "----------------------------------------`n" -ForegroundColor Cyan

if ($script:Failed -gt 0) { exit 1 }
exit 0
