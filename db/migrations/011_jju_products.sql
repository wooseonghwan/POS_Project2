-- 011_jju_products.sql
-- 신규 POS분류 'JJU'(애기) 가격대별 상품 4건 일괄 등록.

INSERT INTO code_tb (code_gbn, code_cd, code_nm, sort_no) VALUES
    ('POSCAT', 'JJU', '애기', 11)
ON DUPLICATE KEY UPDATE code_nm = VALUES(code_nm);

INSERT INTO product_tb (barcode, poscat_cd, name, price, stock_qty, use_yn, pos_grid_yn, sort_no) VALUES
('50073', 'JJU', '애기 3000원', 3000, 99, 'Y', 'Y', 0),
('50074', 'JJU', '애기 3500원', 3500, 99, 'Y', 'Y', 1),
('50075', 'JJU', '애기 4000원', 4000, 99, 'Y', 'Y', 2),
('50076', 'JJU', '애기 4500원', 4500, 99, 'Y', 'Y', 3)
ON DUPLICATE KEY UPDATE
    poscat_cd = VALUES(poscat_cd), name = VALUES(name), price = VALUES(price),
    stock_qty = VALUES(stock_qty), use_yn = 'Y', pos_grid_yn = VALUES(pos_grid_yn),
    sort_no = VALUES(sort_no);
