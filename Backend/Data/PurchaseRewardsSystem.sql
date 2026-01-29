-- ============================================
-- PURCHASE-BASED REFERRAL REWARDS SYSTEM
-- When users buy credits, both referrer and
-- referred users get rewarded based on plan
-- ============================================

-- ───────────────────────────────────────────────────────────────────
-- TABLE: purchase_referral_settings
-- Admin-configurable reward amounts per plan
-- ───────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS `purchase_referral_settings` (
  `id` INT NOT NULL AUTO_INCREMENT,
  `plan_id` INT NOT NULL,
  `referrer_reward_credits` DOUBLE NOT NULL DEFAULT 0 COMMENT 'Credits given to referrer when referee buys this plan',
  `referee_reward_credits` DOUBLE NOT NULL DEFAULT 0 COMMENT 'Credits given to referee when referee buys this plan', 
  `owner_purchase_reward_credits` DOUBLE NOT NULL DEFAULT 0 COMMENT 'Credits given to all referrals when owner buys this plan',
  `is_enabled` TINYINT(1) NOT NULL DEFAULT 1,
  `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (`id`),
  UNIQUE KEY `ix_purchase_referral_settings_plan_id` (`plan_id`),
  CONSTRAINT `fk_purchase_referral_settings_plan` FOREIGN KEY (`plan_id`)
    REFERENCES `stripeplans`(`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

-- ───────────────────────────────────────────────────────────────────
-- TABLE: credit_audit_log
-- SECURITY: Tracks ALL credit modifications with unique IDs
-- Used for 3-level security validation
-- ───────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS `credit_audit_log` (
  `id` VARCHAR(36) NOT NULL COMMENT 'Unique UUID for this credit operation',
  `user_id` INT NOT NULL,
  `operation_type` ENUM('PURCHASE', 'REFERRAL_SIGNUP', 'PURCHASE_REFERRER_REWARD', 'PURCHASE_REFEREE_REWARD', 'PURCHASE_OWNER_REWARD', 'ADMIN_GRANT', 'USAGE_DEDUCT', 'REFUND', 'ADJUSTMENT') NOT NULL,
  `credits_amount` DOUBLE NOT NULL,
  `credits_before` DOUBLE NOT NULL,
  `credits_after` DOUBLE NOT NULL,
  `source_type` VARCHAR(64) NOT NULL COMMENT 'Where this credit came from',
  `source_reference_id` VARCHAR(128) DEFAULT NULL COMMENT 'Reference ID (plan_id, transaction_id, etc)',
  `related_user_id` INT DEFAULT NULL COMMENT 'Related user (referrer/referee)',
  `security_hash` VARCHAR(128) NOT NULL COMMENT 'SHA256 hash for validation',
  `ip_address` VARCHAR(45) DEFAULT NULL,
  `user_agent` VARCHAR(512) DEFAULT NULL,
  `is_validated` TINYINT(1) NOT NULL DEFAULT 0 COMMENT 'Security validation status',
  `validated_at` DATETIME DEFAULT NULL,
  `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`id`),
  INDEX `ix_credit_audit_log_user_id` (`user_id`),
  INDEX `ix_credit_audit_log_operation_type` (`operation_type`),
  INDEX `ix_credit_audit_log_created_at` (`created_at`),
  INDEX `ix_credit_audit_log_security_hash` (`security_hash`),
  CONSTRAINT `fk_credit_audit_log_user` FOREIGN KEY (`user_id`)
    REFERENCES `users`(`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

-- ───────────────────────────────────────────────────────────────────
-- TABLE: purchase_referral_transactions  
-- Tracks purchase-based reward distributions
-- ───────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS `purchase_referral_transactions` (
  `id` INT NOT NULL AUTO_INCREMENT,
  `audit_log_id` VARCHAR(36) NOT NULL COMMENT 'Links to credit_audit_log for security',
  `purchaser_id` INT NOT NULL COMMENT 'User who made the purchase',
  `beneficiary_id` INT NOT NULL COMMENT 'User who received the reward',
  `plan_id` INT NOT NULL,
  `reward_type` ENUM('REFERRER_REWARD', 'REFEREE_REWARD', 'OWNER_PURCHASE_REWARD') NOT NULL,
  `credits_awarded` DOUBLE NOT NULL,
  `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`id`),
  INDEX `ix_purchase_referral_tx_purchaser` (`purchaser_id`),
  INDEX `ix_purchase_referral_tx_beneficiary` (`beneficiary_id`),
  INDEX `ix_purchase_referral_tx_plan` (`plan_id`),
  INDEX `ix_purchase_referral_tx_audit` (`audit_log_id`),
  CONSTRAINT `fk_purchase_referral_tx_purchaser` FOREIGN KEY (`purchaser_id`)
    REFERENCES `users`(`id`) ON DELETE CASCADE,
  CONSTRAINT `fk_purchase_referral_tx_beneficiary` FOREIGN KEY (`beneficiary_id`)
    REFERENCES `users`(`id`) ON DELETE CASCADE,
  CONSTRAINT `fk_purchase_referral_tx_plan` FOREIGN KEY (`plan_id`)
    REFERENCES `stripeplans`(`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

-- ───────────────────────────────────────────────────────────────────
-- TABLE: credit_security_tokens
-- Prevents replay attacks and unauthorized credit modifications
-- ───────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS `credit_security_tokens` (
  `id` VARCHAR(36) NOT NULL,
  `user_id` INT NOT NULL,
  `token_hash` VARCHAR(128) NOT NULL COMMENT 'Hashed security token',
  `operation_type` VARCHAR(64) NOT NULL,
  `is_used` TINYINT(1) NOT NULL DEFAULT 0,
  `expires_at` DATETIME NOT NULL,
  `used_at` DATETIME DEFAULT NULL,
  `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`id`),
  INDEX `ix_credit_security_tokens_user` (`user_id`),
  INDEX `ix_credit_security_tokens_hash` (`token_hash`),
  INDEX `ix_credit_security_tokens_expires` (`expires_at`),
  CONSTRAINT `fk_credit_security_tokens_user` FOREIGN KEY (`user_id`)
    REFERENCES `users`(`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

-- ───────────────────────────────────────────────────────────────────
-- Initialize default settings for existing plans
-- ───────────────────────────────────────────────────────────────────
INSERT INTO `purchase_referral_settings` (`plan_id`, `referrer_reward_credits`, `referee_reward_credits`, `owner_purchase_reward_credits`, `is_enabled`)
SELECT `Id`, 0, 0, 0, 1 FROM `stripeplans`
ON DUPLICATE KEY UPDATE `plan_id` = `plan_id`;
