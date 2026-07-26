-- 005_add_installment_months.sql
-- 카드결제 시 선택한 할부개월(0=일시불)을 매출 건별로 저장한다.
-- 실행: mysql -h <host> -u <user> -p <database> < 005_add_installment_months.sql
-- 주의: 이 마이그레이션은 비멱등(non-idempotent)이다. 실제 MySQL 8은 ADD COLUMN IF NOT EXISTS를 지원하지 않으므로
--       재실행 시 "duplicate column" 오류가 나는 것이 정상이며, 스키마 문제가 아니다. 한 번만 실행할 것.

ALTER TABLE sales_header_tb
    ADD COLUMN installment_months INT NOT NULL DEFAULT 0 AFTER van_code;
