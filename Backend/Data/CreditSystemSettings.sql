-- ============================================================================
-- CREDIT SYSTEM SETTINGS TABLE
-- Singleton table for admin-configurable credit costs
-- ============================================================================

CREATE TABLE IF NOT EXISTS `credit_system_settings` (
  `id` INT NOT NULL DEFAULT 1,
  `free_daily_credits` DOUBLE NOT NULL DEFAULT 50.0,
  `daily_reset_hour_utc` INT NOT NULL DEFAULT 0,
  `new_user_credits` DOUBLE NOT NULL DEFAULT 50.0,
  `cost_per_message` DOUBLE NOT NULL DEFAULT 0.01,
  `cost_per_token` DOUBLE NOT NULL DEFAULT 0.001,
  `project_creation_base_cost` DOUBLE NOT NULL DEFAULT 1.0,
  `code_analysis_cost` DOUBLE NOT NULL DEFAULT 0.5,
  `updated_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

-- ============================================================================
-- SEED DEFAULT VALUES
-- Insert singleton row with default values (id=1)
-- ============================================================================

INSERT INTO `credit_system_settings` (
  `id`,
  `free_daily_credits`,
  `daily_reset_hour_utc`,
  `new_user_credits`,
  `cost_per_message`,
  `cost_per_token`,
  `project_creation_base_cost`,
  `code_analysis_cost`
) VALUES (
  1,
  50.0,   -- free_daily_credits
  0,      -- daily_reset_hour_utc (midnight UTC)
  50.0,   -- new_user_credits
  0.01,   -- cost_per_message
  0.001,  -- cost_per_token
  1.0,    -- project_creation_base_cost
  0.5     -- code_analysis_cost
) ON DUPLICATE KEY UPDATE
  `updated_at` = CURRENT_TIMESTAMP;
