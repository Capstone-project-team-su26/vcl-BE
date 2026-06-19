# PowerShell Integration Test for Pricing Rules & Quotation Integration
$ErrorActionPreference = "Stop"
$baseUrl = "http://localhost:5209"

# Bypass SSL validation check for localhost self-signed HTTPS certificate
[System.Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }

Write-Host "=== STARTING PRICING RULES INTEGRATION TESTS ===" -ForegroundColor Cyan

# 1. Seed accounts
Write-Host "1. Seeding Admin and Customer..."
$seedAdmin = Invoke-RestMethod -Uri "$baseUrl/api/Test/seed-admin" -Method Get
$seedCustomer = Invoke-RestMethod -Uri "$baseUrl/api/Test/seed-customer" -Method Get
Write-Host "   Admin Seed: $($seedAdmin.message)"
Write-Host "   Customer Seed: $($seedCustomer.message)"

# 2. Authenticate
Write-Host "2. Authenticating..."
$adminLogin = Invoke-RestMethod -Uri "$baseUrl/api/Auth/login" -Method Post -Body (@{ email = "admin@test.com"; password = "Admin123" } | ConvertTo-Json) -ContentType "application/json"
$adminToken = $adminLogin.token

$customerLogin = Invoke-RestMethod -Uri "$baseUrl/api/Auth/login" -Method Post -Body (@{ email = "customer@test.com"; password = "Customer123" } | ConvertTo-Json) -ContentType "application/json"
$customerToken = $customerLogin.token

$adminHeaders = @{ Authorization = "Bearer $adminToken" }
$customerHeaders = @{ Authorization = "Bearer $customerToken" }

# 3. Test Authorization (Customer should receive 403 Forbidden)
Write-Host "3. Testing Authorization (Customer role blocking)..."
try {
    Invoke-RestMethod -Uri "$baseUrl/api/pricing-rules" -Method Get -Headers $customerHeaders
    Write-Error "CRITICAL FAILURE: Customer was able to access pricing-rules GET endpoint!"
    exit 1
} catch {
    $statusCode = $_.Exception.Response.StatusCode.Value__
    if ($statusCode -eq 403 -or $statusCode -eq 401) {
        Write-Host "   SUCCESS: Customer access blocked with status code $statusCode" -ForegroundColor Green
    } else {
        Write-Error "Unexpected status code for customer: $statusCode"
        exit 1
    }
}

# 4. Cleanup old test rules if any
Write-Host "4. Cleaning up old test rules..."
$existingRules = Invoke-RestMethod -Uri "$baseUrl/api/pricing-rules" -Method Get -Headers $adminHeaders
foreach ($rule in $existingRules) {
    if ($rule.shippingServiceType -eq "Air-Test" -or $rule.shippingServiceType -eq "Sea-Test") {
        Write-Host "   Cleaning up rule: $($rule.id)"
        Invoke-RestMethod -Uri "$baseUrl/api/pricing-rules/$($rule.id)" -Method Delete -Headers $adminHeaders | Out-Null
    }
}

# 5. Create Pricing Rule
Write-Host "5. Creating a new Pricing Rule (Air-Test Express for Vietnam)..."
$ruleBody = @{
    shippingServiceType = "Air-Test"
    consignmentType = "Express"
    route = "Vietnam"
    unitType = "kg"
    pricePerWeight = 60000.00
    pricePerVolume = 120000.00
    serviceFee = 55000.00
    isActive = $true
} | ConvertTo-Json

$createdRule = Invoke-RestMethod -Uri "$baseUrl/api/pricing-rules" -Method Post -Body $ruleBody -ContentType "application/json" -Headers $adminHeaders
Write-Host "   Created Rule ID: $($createdRule.id)" -ForegroundColor Green

# 6. Duplicate prevention check
Write-Host "6. Testing duplicate constraint..."
try {
    Invoke-RestMethod -Uri "$baseUrl/api/pricing-rules" -Method Post -Body $ruleBody -ContentType "application/json" -Headers $adminHeaders
    Write-Error "CRITICAL FAILURE: Duplicate pricing rule creation succeeded!"
    exit 1
} catch {
    $statusCode = $_.Exception.Response.StatusCode.Value__
    if ($statusCode -eq 400) {
        Write-Host "   SUCCESS: Duplicate creation blocked with 400 Bad Request" -ForegroundColor Green
    } else {
        Write-Error "Unexpected status code for duplicate check: $statusCode"
        exit 1
    }
}

# 7. GetById check
Write-Host "7. Testing GetById..."
$fetchedRule = Invoke-RestMethod -Uri "$baseUrl/api/pricing-rules/$($createdRule.id)" -Method Get -Headers $adminHeaders
if ($fetchedRule.shippingServiceType -eq "Air-Test" -and $fetchedRule.pricePerWeight -eq 60000.00) {
    Write-Host "   SUCCESS: Fetched correct pricing rule details" -ForegroundColor Green
} else {
    Write-Error "Pricing rule mismatch"
    exit 1
}

# 8. Create Consignment and Verify Quotation pricing integration
Write-Host "8. Testing Quotation Estimate Integration using the configured Pricing Rule..."
# Note: customer Country is "Vietnam" (matches the Route "Vietnam")
# Order items: weight 2.0kg, volume 0.001 m3 (10x10x10cm = 1000cm3 = 0.001 m3).
# Volumetric weight = 1000 / 5000 = 0.2kg.
# Chargeable weight = Max(2.0, 0.2) = 2.0kg.
# Pricing rule weight price = 60000. Weight charge = 2 * 60000 = 120000.
# Pricing rule volume price = 120000. Volume charge = (1000 / 1000000) * 120000 = 120.
# Estimated freight charge = Max(120000, 120) = 120000.
# Service fee = 55000 (from pricing rule, not default 50000).
# Declared value = 100000. Tax (10%) = 10000.
# Total cost = 120000 + 55000 + 10000 = 185000.

$consignmentBody = @{
    shippingOption = "Express"
    note = "Testing pricing rule integration"
    items = @(
        @{
            productName = "Smartphone Test"
            productType = "Electronics"
            quantity = 1
            weight = 2.0
            width = 10
            height = 10
            length = 10
            declaredValue = 100000
        }
    )
} | ConvertTo-Json

$consignmentResult = Invoke-RestMethod -Uri "$baseUrl/api/orders/consignments" -Method Post -Body $consignmentBody -ContentType "application/json" -Headers $customerHeaders
$quotation = $consignmentResult.data.quotation

Write-Host "   Calculated Freight Charge: $($quotation.estimatedFreightCharge) (Expected: 120000)"
Write-Host "   Calculated Service Fee: $($quotation.serviceFee) (Expected: 55000)"
Write-Host "   Calculated Total Estimated Cost: $($quotation.totalEstimatedCost) (Expected: 185000)"

if ($quotation.estimatedFreightCharge -eq 120000 -and $quotation.serviceFee -eq 55000 -and $quotation.totalEstimatedCost -eq 185000) {
    Write-Host "   SUCCESS: Quotation pricing successfully matched and calculated using the DB pricing rule!" -ForegroundColor Green
} else {
    Write-Error "Quotation calculation mismatch! Custom pricing rule was not correctly applied."
    exit 1
}

# 9. Update Pricing Rule
Write-Host "9. Updating Pricing Rule..."
$updateBody = @{
    shippingServiceType = "Air-Test"
    consignmentType = "Express"
    route = "Vietnam"
    unitType = "kg"
    pricePerWeight = 70000.00
    pricePerVolume = 150000.00
    serviceFee = 65000.00
    isActive = $true
} | ConvertTo-Json

Invoke-RestMethod -Uri "$baseUrl/api/pricing-rules/$($createdRule.id)" -Method Put -Body $updateBody -ContentType "application/json" -Headers $adminHeaders | Out-Null
Write-Host "   Rule updated successfully."

# 10. Re-verify Quotation pricing using updated rule
Write-Host "10. Creating second Consignment and verifying updated Quotation pricing..."
# Updated values: PricePerWeight = 70000, ServiceFee = 65000.
# Weight charge = 2 * 70000 = 140000.
# Service fee = 65000.
# Tax = 10000.
# Total cost = 140000 + 65000 + 10000 = 215000.

$consignmentResult2 = Invoke-RestMethod -Uri "$baseUrl/api/orders/consignments" -Method Post -Body $consignmentBody -ContentType "application/json" -Headers $customerHeaders
$quotation2 = $consignmentResult2.data.quotation

Write-Host "   Calculated Freight Charge: $($quotation2.estimatedFreightCharge) (Expected: 140000)"
Write-Host "   Calculated Service Fee: $($quotation2.serviceFee) (Expected: 65000)"
Write-Host "   Calculated Total Estimated Cost: $($quotation2.totalEstimatedCost) (Expected: 215000)"

if ($quotation2.estimatedFreightCharge -eq 140000 -and $quotation2.serviceFee -eq 65000 -and $quotation2.totalEstimatedCost -eq 215000) {
    Write-Host "   SUCCESS: Updated quotation pricing successfully matched and calculated!" -ForegroundColor Green
} else {
    Write-Error "Updated Quotation calculation mismatch!"
    exit 1
}

# 11. Delete pricing rule
Write-Host "11. Deleting test pricing rule..."
$deleteResult = Invoke-RestMethod -Uri "$baseUrl/api/pricing-rules/$($createdRule.id)" -Method Delete -Headers $adminHeaders
Write-Host "    Delete result: $($deleteResult.message)" -ForegroundColor Green

# 12. Verify deleted rule is not active / throws 404
Write-Host "12. Verifying deletion throws 404..."
try {
    Invoke-RestMethod -Uri "$baseUrl/api/pricing-rules/$($createdRule.id)" -Method Get -Headers $adminHeaders
    Write-Error "CRITICAL FAILURE: Deleted rule still accessible!"
    exit 1
} catch {
    $statusCode = $_.Exception.Response.StatusCode.Value__
    if ($statusCode -eq 404) {
        Write-Host "    SUCCESS: Fetching deleted rule returned 404 Not Found" -ForegroundColor Green
    } else {
        Write-Error "Unexpected status code: $statusCode"
        exit 1
    }
}

Write-Host "`n=================================================" -ForegroundColor Green
Write-Host " ALL PRICING RULES TEST SCENARIOS COMPLETED!      " -ForegroundColor Green
Write-Host "=================================================" -ForegroundColor Green
