-- 010_product_sort_and_category_visibility.sql
-- 1) POS분류 탭 안에서 상품이 보여지는 순서를 직접 정할 수 있도록 product_tb에 sort_no 추가.
-- 2) 판매화면에 특정 POS분류 탭 자체를 숨길 수 있도록 code_tb에 use_yn 추가(기존 코드는 전부 'Y'로 시작).

ALTER TABLE product_tb ADD COLUMN sort_no INT NOT NULL DEFAULT 0;
ALTER TABLE code_tb ADD COLUMN use_yn CHAR(1) NOT NULL DEFAULT 'Y';
