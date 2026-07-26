-- 006_add_cash_receipt_columns.sql
-- 현금영수증 발행 여부/가맹점/승인번호를 매출 건별로 저장한다.
-- 실행: mysql -h <host> -u <user> -p <database> < 006_add_cash_receipt_columns.sql
-- 주의: 이 마이그레이션은 멱등(idempotent)하지 않다(005와 동일) — 재실행 시 "duplicate column" 에러가 나면
-- 이미 적용된 것이니 정상이다. 신규 실행에서만 사용한다.

ALTER TABLE sales_header_tb
    ADD COLUMN cash_receipt_type VARCHAR(10) NOT NULL DEFAULT 'NONE' AFTER installment_months, -- 'NONE'/'PERSONAL'/'BUSINESS'
    ADD COLUMN cash_receipt_merchant VARCHAR(10) NULL AFTER cash_receipt_type, -- 'CARD1'/'CARD2' — van_config_tb 조회 키
    ADD COLUMN cash_receipt_approval_no VARCHAR(40) NULL AFTER cash_receipt_merchant,
    ADD COLUMN cash_receipt_approval_date VARCHAR(6) NULL AFTER cash_receipt_approval_no; -- YYMMDD(KICC 응답 R07 앞 6자리) — 향후 B2 취소에 필요
