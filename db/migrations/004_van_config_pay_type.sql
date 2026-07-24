-- 004_van_config_pay_type.sql
-- van_config_tb에 pay_type 컬럼 추가: 카드결제1/2가 같은 물리 단말기(van_code='KICC')를
-- 공유하면서도 서로 다른 가맹점(TID/사업자번호) 앞으로 승인 요청을 보내야 하기 때문.
-- 실행: mysql -h <host> -u <user> -p <database> < 004_van_config_pay_type.sql

ALTER TABLE van_config_tb
    ADD COLUMN pay_type VARCHAR(10) NOT NULL DEFAULT 'CARD1' AFTER van_code,
    ADD COLUMN business_no VARCHAR(10) NULL AFTER terminal_id,
    DROP PRIMARY KEY,
    ADD PRIMARY KEY (pos_cd, van_code, pay_type);

-- 대원수산(카드결제1) / 대원낚시마트(카드결제2) 실제 가맹점 정보 시드
INSERT INTO van_config_tb (pos_cd, van_code, pay_type, terminal_id, business_no) VALUES
    ('1', 'KICC', 'CARD1', '2977338', '3169055788'),
    ('1', 'KICC', 'CARD2', '2977340', '3160326930')
ON DUPLICATE KEY UPDATE terminal_id = VALUES(terminal_id), business_no = VALUES(business_no);
