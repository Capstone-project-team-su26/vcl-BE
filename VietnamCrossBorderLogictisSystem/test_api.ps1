# Powershell Integration Test for Restricted Items Feature

$baseUrl = "https://api-vcl.purintech.id.vn"

Write-Host "--- Starting Backend Server Verification ---" -ForegroundColor Cyan

# 1. Seeding roles
Write-Host "Seeding Admin and Customer..."
$seedAdmin = Invoke-RestMethod -Uri "$baseUrl/api/Test/seed-admin" -Method Get
$seedCustomer = Invoke-RestMethod -Uri "$baseUrl/api/Test/seed-customer" -Method Get

Write-Host "Admin Seed Result: $($seedAdmin.message)"
Write-Host "Customer Seed Result: $($seedCustomer.message)"

# 2. Login
Write-Host "Authenticating users..."
$adminLoginBody = @{
    email = "admin@test.com"
    password = "Admin123"
} | ConvertTo-Json
$adminTokenResp = Invoke-RestMethod -Uri "$baseUrl/api/Auth/login" -Method Post -Body $adminLoginBody -ContentType "application/json"
$adminToken = $adminTokenResp.token

$customerLoginBody = @{
    email = "customer@test.com"
    password = "Customer123"
} | ConvertTo-Json
$customerTokenResp = Invoke-RestMethod -Uri "$baseUrl/api/Auth/login" -Method Post -Body $customerLoginBody -ContentType "application/json"
$customerToken = $customerTokenResp.token

$adminHeaders = @{
    Authorization = "Bearer $adminToken"
}
$customerHeaders = @{
    Authorization = "Bearer $customerToken"
}

# 3. Test Authorization (Customer should be unauthorized)
Write-Host "Checking Authorization..."
try {
    Invoke-RestMethod -Uri "$baseUrl/api/restricted-items" -Method Get -Headers $customerHeaders
    Write-Error "CRITICAL FAILURE: Customer was able to access restricted-items endpoint!"
    exit 1
} catch {
    $statusCode = $_.Exception.Response.StatusCode.Value__
    if ($statusCode -eq 403 -or $statusCode -eq 401) {
        Write-Host "SUCCESS: Customer access blocked with status code $statusCode" -ForegroundColor Green
    } else {
        Write-Error "Unexpected status code for customer: $statusCode"
        exit 1
    }
}

# 4. Test GetAll (Admin should succeed)
Write-Host "Testing GetAll as Admin..."
$allItems = Invoke-RestMethod -Uri "$baseUrl/api/restricted-items" -Method Get -Headers $adminHeaders
Write-Host "Initial Restricted Items Count: $($allItems.Count)"

# Clean up any existing test items first to avoid duplicate errors if script is run multiple times
foreach ($item in $allItems) {
    if ($item.itemName -eq "Pin Lithium Test" -or $item.itemName -eq "Thuoc La Test") {
        Write-Host "Cleaning up old test item: $($item.itemName)"
        Invoke-RestMethod -Uri "$baseUrl/api/restricted-items/$($item.id)" -Method Delete -Headers $adminHeaders | Out-Null
    }
}

# 5. Create new restricted item
Write-Host "Creating a new restricted item (Pin Lithium)..."
$createBody = @{
    itemName = "Pin Lithium Test"
    country = "Vietnam"
    restrictionType = "BANNED"
    note = "Banned on aircraft"
    isActive = $true
} | ConvertTo-Json

$createdItem = Invoke-RestMethod -Uri "$baseUrl/api/restricted-items" -Method Post -Body $createBody -ContentType "application/json" -Headers $adminHeaders
Write-Host "Created Item ID: $($createdItem.id)" -ForegroundColor Green

# 6. Test duplicate constraint (ItemName + Country + RestrictionType)
Write-Host "Testing duplicate constraint..."
try {
    Invoke-RestMethod -Uri "$baseUrl/api/restricted-items" -Method Post -Body $createBody -ContentType "application/json" -Headers $adminHeaders
    Write-Error "CRITICAL FAILURE: Duplicate item creation succeeded!"
    exit 1
} catch {
    $statusCode = $_.Exception.Response.StatusCode.Value__
    if ($statusCode -eq 400) {
        Write-Host "SUCCESS: Duplicate creation blocked with 400 Bad Request" -ForegroundColor Green
    } else {
        Write-Error "Unexpected status code for duplicate: $statusCode"
        exit 1
    }
}

# 7. GetById
Write-Host "Testing GetById..."
$fetchedItem = Invoke-RestMethod -Uri "$baseUrl/api/restricted-items/$($createdItem.id)" -Method Get -Headers $adminHeaders
if ($fetchedItem.itemName -eq "Pin Lithium Test") {
    Write-Host "SUCCESS: Fetched correct item" -ForegroundColor Green
} else {
    Write-Error "Fetched item mismatch: $($fetchedItem.itemName)"
    exit 1
}

# 8. Test validation rules (e.g. invalid RestrictionType)
Write-Host "Testing invalid validation fields..."
$invalidBody = @{
    itemName = "Valid Name"
    restrictionType = "INVALID_TYPE" # Should be BANNED, RESTRICTED, or WARNING
} | ConvertTo-Json
try {
    Invoke-RestMethod -Uri "$baseUrl/api/restricted-items" -Method Post -Body $invalidBody -ContentType "application/json" -Headers $adminHeaders
    Write-Error "CRITICAL FAILURE: Invalid restriction type creation succeeded!"
    exit 1
} catch {
    $statusCode = $_.Exception.Response.StatusCode.Value__
    if ($statusCode -eq 400) {
        Write-Host "SUCCESS: Invalid restriction type blocked with 400 Bad Request" -ForegroundColor Green
    } else {
        Write-Error "Unexpected status code for validation: $statusCode"
        exit 1
    }
}

# 9. Test Consignment Check logic (banned item is blocked)
Write-Host "Testing Consignment Creation with Banned Item..."
$consignmentBody = @{
    shippingOption = "Express"
    note = "Testing validation"
    items = @(
        @{
            productName = "Pin Lithium Test"
            productType = "Electronics"
            quantity = 1
            weight = 1.5
            width = 10
            height = 10
            length = 10
            declaredValue = 200000
        }
    )
} | ConvertTo-Json

try {
    Invoke-RestMethod -Uri "$baseUrl/api/orders/consignments" -Method Post -Body $consignmentBody -ContentType "application/json" -Headers $customerHeaders
    Write-Error "CRITICAL FAILURE: Consignment containing banned Pin Lithium Test was successfully created!"
    exit 1
} catch {
    $statusCode = $_.Exception.Response.StatusCode.Value__
    if ($statusCode -eq 400) {
        Write-Host "SUCCESS: Consignment with banned item blocked with 400 Bad Request" -ForegroundColor Green
    } else {
        Write-Error "Unexpected status code for consignment check: $statusCode"
        exit 1
    }
}

# 10. Disable item (Soft disable) and retest consignment creation
Write-Host "Disabling Pin Lithium Test..."
$updateBody = @{
    itemName = "Pin Lithium Test"
    country = "Vietnam"
    restrictionType = "BANNED"
    note = "Banned on aircraft (disabled for now)"
    isActive = $false
} | ConvertTo-Json

Invoke-RestMethod -Uri "$baseUrl/api/restricted-items/$($createdItem.id)" -Method Put -Body $updateBody -ContentType "application/json" -Headers $adminHeaders | Out-Null
Write-Host "Item disabled successfully."

Write-Host "Testing Consignment Creation again with disabled Banned Item..."
try {
    $consignmentResult = Invoke-RestMethod -Uri "$baseUrl/api/orders/consignments" -Method Post -Body $consignmentBody -ContentType "application/json" -Headers $customerHeaders
    Write-Host "SUCCESS: Consignment created successfully while banned item is disabled!" -ForegroundColor Green
    Write-Host "Consignment Order Status: $($consignmentResult.data.status)"
} catch {
    Write-Error "Consignment creation failed even though the banned item is disabled: $_"
    exit 1
}

# 11. Delete item
Write-Host "Deleting test item..."
$deleteResult = Invoke-RestMethod -Uri "$baseUrl/api/restricted-items/$($createdItem.id)" -Method Delete -Headers $adminHeaders
Write-Host "Delete Result: $($deleteResult.message)" -ForegroundColor Green

# 12. Verify delete (returns 404)
Write-Host "Verifying deletion return 404..."
try {
    Invoke-RestMethod -Uri "$baseUrl/api/restricted-items/$($createdItem.id)" -Method Get -Headers $adminHeaders
    Write-Error "CRITICAL FAILURE: Restricted item still exists after deletion!"
    exit 1
} catch {
    $statusCode = $_.Exception.Response.StatusCode.Value__
    if ($statusCode -eq 404) {
        Write-Host "SUCCESS: Fetching deleted item returned 404 Not Found" -ForegroundColor Green
    } else {
        Write-Error "Unexpected status code for deleted item check: $statusCode"
        exit 1
    }
}

Write-Host "`n=================================================" -ForegroundColor Green
Write-Host " ALL TEST SCENARIOS COMPLETED SUCCESSFULLY! " -ForegroundColor Green
Write-Host "=================================================" -ForegroundColor Green
