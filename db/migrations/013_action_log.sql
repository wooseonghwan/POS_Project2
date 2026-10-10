-- 모든 주요 액션(메뉴 이동, 결제, 결제취소, 상품등록/수정, 직원등록/수정, 시재입력, 영수증인쇄,
-- 처리되지 않은 예외 등)을 한 테이블에 기록한다. 문제가 발생했을 때 "언제 누가 어떤 액션을
-- 했는지" 바로 조회해서 원인을 추적하기 위한 용도.
CREATE TABLE IF NOT EXISTS action_log_tb (
    log_no          BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    log_dt          DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    pos_cd          VARCHAR(4) NULL,
    staff_cd        VARCHAR(10) NULL,
    -- 예: NAVIGATE, LOGIN, LOGOUT, SALE_CASH, SALE_CARD, SALE_CANCEL, CASH_RECEIPT_ISSUE,
    --     RECEIPT_PRINT, PRODUCT_SAVE, STAFF_SAVE, CASH_DRAWER_SAVE, UNHANDLED_EXCEPTION
    action_type     VARCHAR(40) NOT NULL,
    -- 사람이 읽을 수 있는 상세 설명(예: "상품수정: 바코드=12345, 상품명=파워에이드, 단가=1,500원")
    action_detail   TEXT NULL,
    result_status   VARCHAR(10) NOT NULL DEFAULT 'SUCCESS',
    error_message   TEXT NULL,
    PRIMARY KEY (log_no),
    INDEX idx_action_log_log_dt (log_dt),
    INDEX idx_action_log_action_type (action_type),
    INDEX idx_action_log_result_status (result_status)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
