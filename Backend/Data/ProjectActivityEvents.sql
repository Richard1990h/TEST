-- ============================================================================
-- PROJECT ACTIVITY EVENTS
-- Unified activity stream for Analyzer / Factory / Chat pipelines.
-- Additive-only table.
-- ============================================================================

CREATE TABLE IF NOT EXISTS project_activity_events (
    id BIGINT PRIMARY KEY AUTO_INCREMENT,
    session_id VARCHAR(64) NOT NULL,
    user_id INT NOT NULL,
    source VARCHAR(32) NOT NULL,
    level VARCHAR(16) NOT NULL,
    phase VARCHAR(64) NULL,
    message TEXT NOT NULL,
    details_json LONGTEXT NULL,
    created_utc DATETIME NOT NULL,

    INDEX ix_project_activity_session (session_id),
    INDEX ix_project_activity_user (user_id),
    INDEX ix_project_activity_source (source)
);
