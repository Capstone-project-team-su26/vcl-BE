-- ==============================================================================
-- Script SQL dọn dẹp và cập nhật giá trị mặc định cho các Ô kệ (Bin)
-- 1. Xóa các ô kệ rác rỗng bị NULL cả max_weight và max_volume (không chứa hàng)
-- 2. Đặt giá trị mặc định chuẩn kho (max_volume = 500,000 cm³ ~ 0.5 m³, max_weight = 100 kg)
--    cho các ô kệ hợp lệ còn lại nếu đang bị NULL
-- ==============================================================================

BEGIN TRANSACTION;

-- 1. Gỡ liên kết bin_id trên Sơ đồ vị trí kho (Warehouse Location Layout) với ô kệ rác rỗng
UPDATE WAREHOUSE_LOCATION_LAYOUTS 
SET bin_id = NULL 
WHERE bin_id IN (
    SELECT b.id 
    FROM BINS b
    LEFT JOIN INVENTORIES i ON b.id = i.bin_id AND i.quantity > 0
    LEFT JOIN PARCELS p ON b.id = p.bin_id
    WHERE b.max_weight IS NULL 
      AND b.max_volume IS NULL 
      AND i.id IS NULL 
      AND p.id IS NULL
);

-- 2. Xóa các bản ghi ô kệ rác rỗng chưa cấu hình kích thước khỏi bảng BINS
DELETE FROM BINS 
WHERE id IN (
    SELECT b.id 
    FROM BINS b
    LEFT JOIN INVENTORIES i ON b.id = i.bin_id AND i.quantity > 0
    LEFT JOIN PARCELS p ON b.id = p.bin_id
    WHERE b.max_weight IS NULL 
      AND b.max_volume IS NULL 
      AND i.id IS NULL 
      AND p.id IS NULL
);

-- 3. Cập nhật giá trị mặc định chuẩn kho (max_volume = 500,000 cm³, max_weight = 100 kg) cho tất cả ô kệ còn lại nếu bị NULL
UPDATE BINS 
SET max_volume = ISNULL(max_volume, 500000),
    max_weight = ISNULL(max_weight, 100)
WHERE max_volume IS NULL OR max_weight IS NULL;

COMMIT TRANSACTION;
