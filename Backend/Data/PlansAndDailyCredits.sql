-- ============================================================================
-- ADD-ONLY TABLES for Plans (capabilities) + Daily Credits
-- Compatible with existing stripeplans/userplans/users tables
-- ============================================================================

CREATE TABLE IF NOT EXISTS `stripeplan_policies` (
  `plan_id` INT NOT NULL,
  `plan_name` VARCHAR(128) NOT NULL DEFAULT '',
  `is_unlimited` TINYINT(1) NOT NULL DEFAULT 0,
  `daily_credits` DOUBLE NULL,
  PRIMARY KEY (`plan_id`),
  CONSTRAINT `fk_stripeplan_policies_plan` FOREIGN KEY (`plan_id`)
    REFERENCES `stripeplans`(`Id`) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS `user_daily_credit_state` (
  `user_id` INT NOT NULL,
  `utc_day` DATE NOT NULL,
  `daily_allowance` DOUBLE NOT NULL,
  `daily_remaining` DOUBLE NOT NULL,
  `updated_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`user_id`),
  CONSTRAINT `fk_user_daily_credit_user` FOREIGN KEY (`user_id`)
    REFERENCES `users`(`id`) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS `user_stripe_subscriptions` (
  `id` INT NOT NULL AUTO_INCREMENT,
  `user_id` INT NOT NULL,
  `plan_id` INT NOT NULL,
  `price_id` VARCHAR(128) NOT NULL,
  `subscription_id` VARCHAR(128) NOT NULL,
  `status` VARCHAR(32) NOT NULL DEFAULT 'active',
  `current_period_end_utc` DATETIME NOT NULL,
  `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`id`),
  KEY `ix_user_stripe_subscriptions_user_id` (`user_id`),
  KEY `ix_user_stripe_subscriptions_subscription_id` (`subscription_id`),
  CONSTRAINT `fk_user_stripe_subscriptions_user` FOREIGN KEY (`user_id`)
    REFERENCES `users`(`id`) ON DELETE CASCADE,
  CONSTRAINT `fk_user_stripe_subscriptions_plan` FOREIGN KEY (`plan_id`)
    REFERENCES `stripeplans`(`Id`) ON DELETE CASCADE
);

-- Optional: seed policy for existing plans (edit values as needed)
-- INSERT INTO stripeplan_policies (plan_id, plan_name, is_unlimited, daily_credits)
-- VALUES (1, 'Pro Unlimited', 1, NULL)
-- ON DUPLICATE KEY UPDATE plan_name=VALUES(plan_name), is_unlimited=VALUES(is_unlimited), daily_credits=VALUES(daily_credits);
