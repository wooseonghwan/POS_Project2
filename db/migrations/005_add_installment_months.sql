-- 005_add_installment_months.sql
-- 카드결제 시 선택한 할부개월(0=일시불)을 매출 건별로 저장한다.
-- 실행: mysql -h <host> -u <user> -p <database> < 005_add_installment_months.sql

ALTER TABLE sales_header_tb
    ADD COLUMN installment_months INT NOT NULL DEFAULT 0 AFTER van_code;
