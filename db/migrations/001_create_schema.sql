-- 001_create_schema.sql
-- 대원낚시 POS 최소 스키마 (설계 문서 8절 기준)
-- 실행: mysql -h <host> -u <user> -p <database> < 001_create_schema.sql

CREATE TABLE IF NOT EXISTS staff_tb (
    staff_cd    VARCHAR(10)     NOT NULL,
    staff_name  VARCHAR(50)     NOT NULL,
    pin_hash    VARCHAR(128)    NOT NULL,
    role        VARCHAR(10)     NOT NULL, -- 'ADMIN' or 'STAFF'
    use_yn      CHAR(1)         NOT NULL DEFAULT 'Y',
    created_at  DATETIME        NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (staff_cd)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS pos_terminal_tb (
    pos_cd      VARCHAR(4)      NOT NULL,
    pos_name    VARCHAR(30)     NOT NULL,
    PRIMARY KEY (pos_cd)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS code_tb (
    code_gbn    VARCHAR(10)     NOT NULL, -- 'MAJOR' / 'MINOR' / 'POSCAT'
    code_cd     VARCHAR(20)     NOT NULL,
    code_nm     VARCHAR(50)     NOT NULL,
    sort_no     INT             NOT NULL DEFAULT 0,
    PRIMARY KEY (code_gbn, code_cd)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS product_tb (
    barcode     VARCHAR(30)     NOT NULL,
    major_cd    VARCHAR(20)     NULL,
    minor_cd    VARCHAR(20)     NULL,
    poscat_cd   VARCHAR(20)     NULL,
    name        VARCHAR(100)    NOT NULL,
    price       DECIMAL(12,0)   NOT NULL,
    stock_qty   INT             NOT NULL DEFAULT 0,
    photo_path  VARCHAR(255)    NULL,
    use_yn      CHAR(1)         NOT NULL DEFAULT 'Y',
    created_at  DATETIME        NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at  DATETIME        NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (barcode)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS sales_header_tb (
    sale_no         BIGINT          NOT NULL AUTO_INCREMENT,
    pos_cd          VARCHAR(4)      NOT NULL,
    sale_dt         DATETIME        NOT NULL,
    staff_cd        VARCHAR(10)     NOT NULL,
    total_amt       DECIMAL(12,0)   NOT NULL,
    pay_type        VARCHAR(10)     NOT NULL, -- 'CASH' / 'CARD1' / 'CARD2'
    cash_received   DECIMAL(12,0)   NULL,
    change_amt      DECIMAL(12,0)   NULL,
    van_approval_no VARCHAR(40)     NULL,
    van_code        VARCHAR(10)     NULL, -- 'NCVAN' / 'KICC'
    status          VARCHAR(10)     NOT NULL DEFAULT 'COMPLETE', -- 'COMPLETE' / 'CANCELLED'
    created_at      DATETIME        NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (sale_no)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS sales_detail_tb (
    sale_no      BIGINT          NOT NULL,
    line_no      INT             NOT NULL,
    barcode      VARCHAR(30)     NOT NULL,
    product_name VARCHAR(100)    NOT NULL,
    qty          INT             NOT NULL,
    unit_price   DECIMAL(12,0)   NOT NULL,
    line_amt     DECIMAL(12,0)   NOT NULL,
    PRIMARY KEY (sale_no, line_no)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS held_order_tb (
    hold_no     BIGINT          NOT NULL AUTO_INCREMENT,
    pos_cd      VARCHAR(4)      NOT NULL,
    staff_cd    VARCHAR(10)     NOT NULL,
    held_at     DATETIME        NOT NULL DEFAULT CURRENT_TIMESTAMP,
    memo        VARCHAR(100)    NULL,
    status      VARCHAR(10)     NOT NULL DEFAULT 'HELD', -- 'HELD' / 'RECALLED'
    PRIMARY KEY (hold_no)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS held_order_detail_tb (
    hold_no      BIGINT          NOT NULL,
    line_no      INT             NOT NULL,
    barcode      VARCHAR(30)     NOT NULL,
    product_name VARCHAR(100)    NOT NULL,
    qty          INT             NOT NULL,
    unit_price   DECIMAL(12,0)   NOT NULL,
    PRIMARY KEY (hold_no, line_no)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS printer_config_tb (
    pos_cd              VARCHAR(4)      NOT NULL,
    printer_port        VARCHAR(50)     NULL,
    printer_name        VARCHAR(50)     NULL,
    drawer_kick_enabled CHAR(1)         NOT NULL DEFAULT 'Y',
    PRIMARY KEY (pos_cd)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS receipt_config_tb (
    pos_cd      VARCHAR(4)      NOT NULL,
    header_text VARCHAR(200)    NULL,
    footer_text VARCHAR(200)    NULL,
    PRIMARY KEY (pos_cd)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS van_config_tb (
    pos_cd      VARCHAR(4)      NOT NULL,
    van_code    VARCHAR(10)     NOT NULL, -- 'NCVAN' / 'KICC'
    terminal_id VARCHAR(30)     NULL,
    server_ip   VARCHAR(50)     NULL,
    server_port INT             NULL,
    PRIMARY KEY (pos_cd, van_code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS action_log_tb (
    log_id      BIGINT          NOT NULL AUTO_INCREMENT,
    staff_cd    VARCHAR(10)     NOT NULL,
    action_type VARCHAR(30)     NOT NULL,
    ref_no      VARCHAR(30)     NULL,
    detail      VARCHAR(200)    NULL,
    created_at  DATETIME        NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (log_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS system_info_tb (
    app_version VARCHAR(20)     NOT NULL,
    db_version  VARCHAR(20)     NOT NULL,
    updated_at  DATETIME        NOT NULL DEFAULT CURRENT_TIMESTAMP
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- 시드 데이터: Phase 1 로그인/메뉴 화면 동작 확인용
-- PIN '0000'의 SHA-256("0000:fishingmart-pos-salt") 해시값
-- (PinHasher.Hash("0000")와 반드시 동일해야 한다 — 값이 바뀌면 이 INSERT도 함께 갱신할 것)
INSERT INTO staff_tb (staff_cd, staff_name, pin_hash, role, use_yn)
VALUES ('ADMIN1', '관리자', SHA2('0000:fishingmart-pos-salt', 256), 'ADMIN', 'Y')
ON DUPLICATE KEY UPDATE staff_name = staff_name;

INSERT INTO pos_terminal_tb (pos_cd, pos_name) VALUES
    ('1', 'POS1'),
    ('2', 'POS2')
ON DUPLICATE KEY UPDATE pos_name = VALUES(pos_name);

INSERT INTO system_info_tb (app_version, db_version)
SELECT '0.1.0-phase1', '001'
WHERE NOT EXISTS (SELECT 1 FROM system_info_tb);
