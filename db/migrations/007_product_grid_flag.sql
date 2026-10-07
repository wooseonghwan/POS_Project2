-- 007_product_grid_flag.sql
-- 판매 화면 상품 목록 표시 여부. 'Y'=카테고리 목록에 노출(바코드 없이 판매하는 상품), 'N'=목록에 숨김(바코드로만 판매).
-- 바코드 스캔은 목록 노출 여부와 무관하게 모든 활성 상품에서 찾는다.
-- 실행: HeidiSQL에서 fishingmart_test 선택 후 실행 (재실행 시 "duplicate column" 에러가 나면 이미 적용된 것).

ALTER TABLE product_tb
    ADD COLUMN pos_grid_yn CHAR(1) NOT NULL DEFAULT 'N';

-- 기존 샘플 상품은 지금까지처럼 목록에 계속 보이도록 'Y'로 설정
UPDATE product_tb SET pos_grid_yn = 'Y';
