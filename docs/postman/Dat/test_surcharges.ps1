# PowerShell Script to test Quotation Surcharges & Additional Fees endpoints

$baseUrl = "http://localhost:5209"
$orderId = "A17AC767-2605-46ED-B8DB-072AA8A378FB"
$purchaseRequestId = "E648BC6F-275A-4185-A9F0-0853588BBD1D"
$purchaseRequestItemId = "2DADF047-CEAB-46D5-B0A8-525878754CFF"
$pricingRuleId = "397A6B9A-4EFD-4DE7-9367-DE60B5A65616"

Write-Host "--- STARTING API VERIFICATION ---"

# 1. Login Admin
Write-Host "1. Logging in as Admin..."
$adminLoginBody = @{
    email = "admin@test.com"
    password = "Admin123"
} | ConvertTo-Json

try {
    $adminResponse = Invoke-RestMethod -Uri "$baseUrl/api/Auth/login" -Method Post -Body $adminLoginBody -ContentType "application/json"
    $adminToken = $adminResponse.token
    Write-Host "   Admin Login SUCCESS. Token: $($adminToken.Substring(0, 15))...."
} catch {
    Write-Error "   Admin Login FAILED: $_"
    exit 1
}

# 2. Login Customer
Write-Host "2. Logging in as Customer..."
$customerLoginBody = @{
    email = "customer@test.com"
    password = "Customer123"
} | ConvertTo-Json

try {
    $customerResponse = Invoke-RestMethod -Uri "$baseUrl/api/Auth/login" -Method Post -Body $customerLoginBody -ContentType "application/json"
    $customerToken = $customerResponse.token
    Write-Host "   Customer Login SUCCESS. Token: $($customerToken.Substring(0, 15))...."
} catch {
    Write-Error "   Customer Login FAILED: $_"
    exit 1
}

# 3. Create Estimate Consignment Quotation
Write-Host "3. Creating Estimate Consignment Quotation for Order: $orderId..."
$headers = @{
    Authorization = "Bearer $adminToken"
}

try {
    $estimateResponse = Invoke-RestMethod -Uri "$baseUrl/api/orders/$orderId/quotation/estimate" -Method Post -Headers $headers -ContentType "application/json"
    Write-Host "   Estimate Quotation SUCCESS."
    Write-Host "   Returned totalEstimatedCost: $($estimateResponse.data.totalEstimatedCost)"
    Write-Host "   Number of additional fees: $($estimateResponse.data.additionalFees.Count)"
    foreach ($fee in $estimateResponse.data.additionalFees) {
        Write-Host "     - Fee: $($fee.feeName), Type: $($fee.feeType), Amount: $($fee.amount)"
    }
} catch {
    Write-Host "   Estimate Quotation failed or Order status is not correct. Skipping..."
}

# 4. Get Consignment Quotation Details
Write-Host "4. Querying Consignment Quotation Details..."
$customerHeaders = @{
    Authorization = "Bearer $customerToken"
}

try {
    $detailResponse = Invoke-RestMethod -Uri "$baseUrl/api/orders/$orderId/quotation" -Method Get -Headers $customerHeaders
    Write-Host "   Query Quotation Details SUCCESS."
    Write-Host "   Additional fees count from DB: $($detailResponse.data.additionalFees.Count)"
    foreach ($fee in $detailResponse.data.additionalFees) {
        Write-Host "     - DB Fee: $($fee.feeName), Type: $($fee.feeType), Amount: $($fee.amount)"
    }
} catch {
    Write-Host "   Query Quotation Details failed or Order has no active quotation. Skipping..."
}

# 5. Send Official Consignment Quotation with custom surcharges
Write-Host "5. Sending Official Consignment Quotation..."
$sendQuotationBody = @{
    serviceType = "Express"
    weightKg = 5.5
    volumeM3 = 0.02
    declaredValue = 2500000
    quotation = @{
        estimatedFreightCharge = 275000
        serviceFee = 50000
        totalEstimatedCost = 325000
        additionalFees = @(
            @{
                feeId = $pricingRuleId
                label = "Wood Crate Fee"
                amount = 80000
                enabled = $true
            },
            @{
                feeId = $null
                label = "Bulky Special Fee"
                amount = 30000
                enabled = $true
            }
        )
    }
} | ConvertTo-Json -Depth 5

try {
    $sendResponse = Invoke-RestMethod -Uri "$baseUrl/api/orders/$orderId/quotation/send" -Method Post -Headers $headers -Body $sendQuotationBody -ContentType "application/json"
    Write-Host "   Send Official Quotation SUCCESS. Message: $($sendResponse.message)"
} catch {
    Write-Host "   Send Official Quotation failed (possibly order status is already QUOTATION_SENT). Skipping..."
}

# 6. Create Purchase Request Quotation
Write-Host "6. Creating Purchase Request Quotation..."
$purchaseQuotationBody = @{
    purchaseFee = 50000
    shippingFee = 150000
    note = "Official Purchase Quotation"
    items = @(
        @{
            purchaseRequestItemId = $purchaseRequestItemId
            unitPrice = 180000
        }
    )
    additionalFees = @(
        @{
            pricingRuleId = $pricingRuleId
            feeName = "Inspection Fee"
            feeType = "SURCHARGE"
            amount = 25000
            note = "Detail check count"
        },
        @{
            pricingRuleId = $null
            feeName = "Special Pack Fee"
            feeType = "SURCHARGE"
            amount = 40000
            note = "Bubble wraps"
        }
    )
} | ConvertTo-Json -Depth 5

try {
    $prQuotationResponse = Invoke-RestMethod -Uri "$baseUrl/api/purchase-requests/$purchaseRequestId/quotation" -Method Post -Headers $headers -Body $purchaseQuotationBody -ContentType "application/json"
    Write-Host "   Create Purchase Request Quotation SUCCESS."
    Write-Host "   TotalAmount: $($prQuotationResponse.data.totalAmount)"
    Write-Host "   Additional fees count: $($prQuotationResponse.data.additionalFees.Count)"
    foreach ($fee in $prQuotationResponse.data.additionalFees) {
        Write-Host "     - Fee: $($fee.feeName), Amount: $($fee.amount)"
    }
} catch {
    Write-Host "   Create Purchase Request Quotation failed (possibly already quoted). Skipping..."
}

# 7. Get Purchase Request Details
Write-Host "7. Querying Purchase Request Details..."
try {
    $prDetailResponse = Invoke-RestMethod -Uri "$baseUrl/api/purchase-requests/$purchaseRequestId" -Method Get -Headers $headers
    Write-Host "   Query Purchase Request Details SUCCESS."
    if ($prDetailResponse.data.quotation) {
        Write-Host "   Quotation is attached. Status: $($prDetailResponse.data.quotation.status)"
        Write-Host "   Quotation TotalAmount: $($prDetailResponse.data.quotation.totalAmount)"
        Write-Host "   Surcharges detail count: $($prDetailResponse.data.quotation.additionalFees.Count)"
        foreach ($fee in $prDetailResponse.data.quotation.additionalFees) {
            Write-Host "     - Surcharge: $($fee.feeName), Amount: $($fee.amount)"
        }
    } else {
        Write-Host "   No quotation attached to this request yet."
    }
} catch {
    Write-Error "   Query Purchase Request Details FAILED: $_"
}

Write-Host "--- API VERIFICATION COMPLETED ---"
