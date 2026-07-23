-- 003_add_performance_indexes.sql
-- 조회 성능을 위한 인덱스 추가 (전수 쿼리 검증 결과)
-- 실행: mysql -h <host> -u <user> -p <database> < 003_add_performance_indexes.sql

-- 로그인(PIN 조회): staff_tb.pin_hash로 조회하는데 인덱스가 없어 풀스캔이었음
ALTER TABLE staff_tb ADD INDEX idx_staff_pin_hash (pin_hash);

-- 판매/재고 화면 진입 시 상품 목록 조회(use_yn='Y' 필터 + poscat_cd 정렬)
ALTER TABLE product_tb ADD INDEX idx_product_active_cat (use_yn, poscat_cd);

-- 매출관리 화면(일일/월간 조회): sale_dt 범위 + status 필터. 매출 데이터가
-- 계속 쌓이는 테이블이라 인덱스 없이는 데이터가 늘수록 점점 느려짐
ALTER TABLE sales_header_tb ADD INDEX idx_sales_status_dt (status, sale_dt);

-- 보류 목록 조회: pos_cd + status 필터
ALTER TABLE held_order_tb ADD INDEX idx_held_pos_status (pos_cd, status);
