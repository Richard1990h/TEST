-- Knowledge schema v1 (MySQL)
-- Run this once on your database (or use EF migrations if you prefer).

CREATE TABLE IF NOT EXISTS knowledge_entries (
  id INT AUTO_INCREMENT PRIMARY KEY,
  `key` VARCHAR(255) NOT NULL,
  category VARCHAR(100) NOT NULL DEFAULT 'general',
  answer LONGTEXT NOT NULL,
  aliases TEXT NULL,
  confidence DOUBLE NOT NULL DEFAULT 0.6,
  source VARCHAR(50) NOT NULL DEFAULT 'manual',
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  last_used_at DATETIME NULL,
  times_used INT NOT NULL DEFAULT 0,
  INDEX ix_knowledge_entries_key (`key`),
  INDEX ix_knowledge_entries_category (category)
);

CREATE TABLE IF NOT EXISTS learned_knowledge (
  id INT AUTO_INCREMENT PRIMARY KEY,
  normalized_key VARCHAR(500) NOT NULL,
  question LONGTEXT NOT NULL,
  answer LONGTEXT NOT NULL,
  source VARCHAR(50) NOT NULL DEFAULT 'llm',
  confidence DOUBLE NOT NULL DEFAULT 0.55,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  last_used_at DATETIME NULL,
  times_used INT NOT NULL DEFAULT 0,
  last_verified_at DATETIME NULL,
  INDEX ix_learned_knowledge_normkey (normalized_key)
);

CREATE TABLE IF NOT EXISTS fact_entries (
  id INT AUTO_INCREMENT PRIMARY KEY,
  subject VARCHAR(255) NOT NULL,
  property VARCHAR(255) NOT NULL,
  value LONGTEXT NOT NULL,
  source VARCHAR(50) NOT NULL DEFAULT 'manual',
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  valid_until DATETIME NULL,
  last_used_at DATETIME NULL,
  times_used INT NOT NULL DEFAULT 0,
  INDEX ix_fact_entries_subject_property (subject, property)
);

CREATE TABLE IF NOT EXISTS code_rules (
  id INT AUTO_INCREMENT PRIMARY KEY,
  language VARCHAR(50) NOT NULL DEFAULT 'csharp',
  error_code VARCHAR(50) NOT NULL,
  pattern TEXT NULL,
  fix_explanation LONGTEXT NOT NULL,
  fix_template LONGTEXT NULL,
  source VARCHAR(50) NOT NULL DEFAULT 'manual',
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  INDEX ix_code_rules_lang_code (language, error_code)
);
