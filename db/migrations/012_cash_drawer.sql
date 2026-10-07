-- 012_cash_drawer.sql
-- 포스별/영업일별 시작 현금(시재) 금액 기록용 테이블.

CREATE TABLE IF NOT EXISTS cash_drawer_tb (
    pos_cd          VARCHAR(4)      NOT NULL,
    business_date   DATE            NOT NULL,
    opening_amount  DECIMAL(12,0)   NOT NULL DEFAULT 0,
    staff_cd        VARCHAR(10)     NULL,
    updated_at      DATETIME        NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (pos_cd, business_date)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
