using Microsoft.EntityFrameworkCore;
using LittleHelperAI.Models;
using LittleHelperAI.Shared.Models; // Feedback, Knowledge, StripePlan models

namespace LittleHelperAI.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        // ============================================================================
        // CORE ENTITIES
        // ============================================================================
        public DbSet<User> Users { get; set; }
        public DbSet<Feedback> Feedback { get; set; }
        public DbSet<Knowledge> Knowledge { get; set; }

        public DbSet<KnowledgeEntry> KnowledgeEntries { get; set; }
        public DbSet<LearnedKnowledge> LearnedKnowledge { get; set; }
        public DbSet<FactEntry> FactEntries { get; set; }
        public DbSet<CodeRule> CodeRules { get; set; }

        public DbSet<ChatHistory> ChatHistory { get; set; }
        public DbSet<StripePlan> StripePlans { get; set; }
        public DbSet<UserPlan> UserPlans { get; set; }
        public DbSet<StripePlanPolicy> StripePlanPolicies { get; set; }
        public DbSet<UserDailyCreditState> UserDailyCreditStates { get; set; }
        public DbSet<UserStripeSubscription> UserStripeSubscriptions { get; set; }

        // ============================================================================
        // REFERRAL SYSTEM ENTITIES
        // ============================================================================
        public DbSet<ReferralSettings> ReferralSettings { get; set; }
        public DbSet<ReferralTransaction> ReferralTransactions { get; set; }

        // ============================================================================
        // CREDIT SYSTEM SETTINGS
        // ============================================================================
        public DbSet<CreditSystemSettings> CreditSystemSettings { get; set; }

        // ============================================================================
        // PURCHASE-BASED REFERRAL REWARDS (NEW SYSTEM)
        // ============================================================================
        public DbSet<PurchaseReferralSettings> PurchaseReferralSettings { get; set; }
        public DbSet<PurchaseReferralTransaction> PurchaseReferralTransactions { get; set; }
        public DbSet<CreditAuditLog> CreditAuditLogs { get; set; }
        public DbSet<CreditSecurityToken> CreditSecurityTokens { get; set; }

        // ============================================================================
        // FACTORY ENTITIES (Project Generation Pipeline)
        // ============================================================================
        public DbSet<ProjectIntentEntity> ProjectIntents { get; set; }
        public DbSet<FeatureGraphEntity> FeatureGraphs { get; set; }
        public DbSet<GeneratedProjectEntity> GeneratedProjects { get; set; }
        public DbSet<LlmCallEntity> LlmCalls { get; set; }
        public DbSet<BuildRepairEntity> BuildRepairs { get; set; }
        public DbSet<CodeKnowledgeEntity> CodeKnowledge { get; set; }
        public DbSet<FeatureTemplateEntity> FeatureTemplates { get; set; }

        // ============================================================================
        // DEAD LETTER QUEUE
        // ============================================================================
        public DbSet<LittleHelperAI.Shared.Models.DeadLetterMessage> DeadLetterMessages { get; set; }

        // ============================================================================
        // PIPELINE V2 ENTITIES
        // ============================================================================
        public DbSet<Entities.PipelineV2Entity> PipelinesV2 { get; set; }
        public DbSet<Entities.PipelineVersionEntity> PipelineVersions { get; set; }
        public DbSet<Entities.PipelineExecutionEntity> PipelineExecutions { get; set; }
        public DbSet<Entities.PipelineStepLogEntity> PipelineStepLogs { get; set; }
        public DbSet<Entities.PipelineMetricEntity> PipelineMetrics { get; set; }


        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ============================================================================
            // CORE ENTITY CONFIGURATIONS
            // ============================================================================

            // 👤 User Table Configuration
            modelBuilder.Entity<User>(entity =>
            {
                entity.ToTable("users");

                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasColumnName("id");
                entity.Property(e => e.Username).HasColumnName("username").IsRequired();
                entity.Property(e => e.Email).HasColumnName("email").IsRequired();
                entity.Property(e => e.PasswordHash).HasColumnName("password_hash").IsRequired();
                entity.Property(e => e.Role).HasColumnName("role").IsRequired();
                entity.Property(e => e.FirstName).HasColumnName("first_name");
                entity.Property(e => e.LastName).HasColumnName("last_name");
                entity.Property(e => e.OS).HasColumnName("OS");
                entity.Property(e => e.Status).HasColumnName("Status");
                entity.Property(e => e.LastLogin).HasColumnName("LastLogin");
                entity.Property(e => e.Credits).HasColumnName("Credits");
                entity.Property(e => e.CreatedAt).HasColumnName("created_at")
                      .HasDefaultValueSql("CURRENT_TIMESTAMP");
                // 🎁 Referral fields
                entity.Property(e => e.ReferralCode).HasColumnName("referral_code").HasMaxLength(20);
                entity.Property(e => e.ReferredByUserId).HasColumnName("referred_by_user_id");
                entity.HasIndex(e => e.ReferralCode).HasDatabaseName("ix_users_referral_code").IsUnique();
            });

            // 📝 Feedback Table Configuration
            modelBuilder.Entity<Feedback>(entity =>
            {
                entity.ToTable("feedback");

                entity.HasKey(f => f.Id);
                entity.Property(f => f.Id).HasColumnName("id");
                entity.Property(f => f.UserId).HasColumnName("user_id");
                entity.Property(f => f.Message).HasColumnName("message").IsRequired();
                entity.Property(f => f.Response).HasColumnName("response").IsRequired();
                entity.Property(f => f.IsHelpful).HasColumnName("is_helpful");
                entity.Property(f => f.CreatedAt).HasColumnName("created_at")
                      .HasDefaultValueSql("CURRENT_TIMESTAMP");
            });

            // 📚 Knowledge Base Table Configuration
            modelBuilder.Entity<Knowledge>(entity =>
            {
                entity.ToTable("knowledge");

                entity.HasKey(k => k.Id);
                entity.Property(k => k.Id).HasColumnName("id");
                entity.Property(k => k.Question).HasColumnName("question").IsRequired();
                entity.Property(k => k.Answer).HasColumnName("answer").IsRequired();
                entity.Property(k => k.AddedByUserId).HasColumnName("added_by_user_id");
                entity.Property(k => k.CreatedAt).HasColumnName("created_at")
                      .HasDefaultValueSql("CURRENT_TIMESTAMP");
            });

            // 💳 StripePlans Table Configuration
            modelBuilder.Entity<StripePlan>(entity =>
            {
                entity.ToTable("stripeplans");

                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasColumnName("Id");
                entity.Property(e => e.Credits).HasColumnName("Credits").IsRequired();
                entity.Property(e => e.PriceId).HasColumnName("PriceId").IsRequired();
                entity.Property(e => e.PlanType).HasColumnName("PlanType").IsRequired();
            });

            // 📚 KnowledgeEntries (dictionary)
            modelBuilder.Entity<KnowledgeEntry>(entity =>
            {
                entity.ToTable("knowledge_entries");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasColumnName("id");
                entity.Property(e => e.Key).HasColumnName("key").IsRequired();
                entity.Property(e => e.Category).HasColumnName("category").IsRequired();
                entity.Property(e => e.Answer).HasColumnName("answer").IsRequired();
                entity.Property(e => e.Aliases).HasColumnName("aliases");
                entity.Property(e => e.Confidence).HasColumnName("confidence");
                entity.Property(e => e.Source).HasColumnName("source");
                entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.Property(e => e.LastUsedAt).HasColumnName("last_used_at");
                entity.Property(e => e.TimesUsed).HasColumnName("times_used");
                entity.HasIndex(e => e.Key).HasDatabaseName("ix_knowledge_entries_key");
                entity.HasIndex(e => e.Category).HasDatabaseName("ix_knowledge_entries_category");
            });

            // 🧠 LearnedKnowledge
            modelBuilder.Entity<LearnedKnowledge>(entity =>
            {
                entity.ToTable("learned_knowledge");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasColumnName("id");
                entity.Property(e => e.NormalizedKey).HasColumnName("normalized_key").IsRequired();
                entity.Property(e => e.Question).HasColumnName("question").IsRequired();
                entity.Property(e => e.Answer).HasColumnName("answer").IsRequired();
                entity.Property(e => e.Source).HasColumnName("source");
                entity.Property(e => e.Confidence).HasColumnName("confidence");
                entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.Property(e => e.LastUsedAt).HasColumnName("last_used_at");
                entity.Property(e => e.TimesUsed).HasColumnName("times_used");
                entity.Property(e => e.LastVerifiedAt).HasColumnName("last_verified_at");
                entity.HasIndex(e => e.NormalizedKey).HasDatabaseName("ix_learned_knowledge_normkey");
            });

            // 🧾 FactEntries
            modelBuilder.Entity<FactEntry>(entity =>
            {
                entity.ToTable("fact_entries");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasColumnName("id");
                entity.Property(e => e.Subject).HasColumnName("subject").IsRequired();
                entity.Property(e => e.Property).HasColumnName("property").IsRequired();
                entity.Property(e => e.Value).HasColumnName("value").IsRequired();
                entity.Property(e => e.Source).HasColumnName("source");
                entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.Property(e => e.ValidUntil).HasColumnName("valid_until");
                entity.Property(e => e.LastUsedAt).HasColumnName("last_used_at");
                entity.Property(e => e.TimesUsed).HasColumnName("times_used");
                entity.HasIndex(e => new { e.Subject, e.Property }).HasDatabaseName("ix_fact_entries_subject_property");
            });

            // 🧩 CodeRules
            modelBuilder.Entity<CodeRule>(entity =>
            {
                entity.ToTable("code_rules");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasColumnName("id");
                entity.Property(e => e.Language).HasColumnName("language").IsRequired();
                entity.Property(e => e.ErrorCode).HasColumnName("error_code").IsRequired();
                entity.Property(e => e.Pattern).HasColumnName("pattern");
                entity.Property(e => e.FixExplanation).HasColumnName("fix_explanation").IsRequired();
                entity.Property(e => e.FixTemplate).HasColumnName("fix_template");
                entity.Property(e => e.Source).HasColumnName("source");
                entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.HasIndex(e => new { e.Language, e.ErrorCode }).HasDatabaseName("ix_code_rules_lang_code");
            });

            // 💳 UserPlans Table Configuration
            modelBuilder.Entity<UserPlan>(entity =>
            {
                entity.ToTable("userplans");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasColumnName("id");
                entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
                entity.Property(e => e.PlanId).HasColumnName("plan_id").IsRequired();
                entity.Property(e => e.PurchasedAt).HasColumnName("purchased_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.Property(e => e.CreditsAdded).HasColumnName("credits_added");
                entity.HasOne(e => e.Plan).WithMany().HasForeignKey(e => e.PlanId);
            });


// 💳 StripePlanPolicies Table Configuration (ADD-ONLY)
modelBuilder.Entity<StripePlanPolicy>(entity =>
{
    entity.ToTable("stripeplan_policies");
    entity.HasKey(e => e.PlanId);
    entity.Property(e => e.PlanId).HasColumnName("plan_id");
    entity.Property(e => e.PlanName).HasColumnName("plan_name");
    entity.Property(e => e.IsUnlimited).HasColumnName("is_unlimited");
    entity.Property(e => e.DailyCredits).HasColumnName("daily_credits");
});

// 🗓️ UserDailyCreditState Table Configuration (ADD-ONLY)
modelBuilder.Entity<UserDailyCreditState>(entity =>
{
    entity.ToTable("user_daily_credit_state");
    entity.HasKey(e => e.UserId);
    entity.Property(e => e.UserId).HasColumnName("user_id");
    entity.Property(e => e.UtcDay).HasColumnName("utc_day");
    entity.Property(e => e.DailyAllowance).HasColumnName("daily_allowance");
    entity.Property(e => e.DailyRemaining).HasColumnName("daily_remaining");
    entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
});

// 🔁 UserStripeSubscription Table Configuration (ADD-ONLY)
modelBuilder.Entity<UserStripeSubscription>(entity =>
{
    entity.ToTable("user_stripe_subscriptions");
    entity.HasKey(e => e.Id);
    entity.Property(e => e.Id).HasColumnName("id");
    entity.Property(e => e.UserId).HasColumnName("user_id");
    entity.Property(e => e.PlanId).HasColumnName("plan_id");
    entity.Property(e => e.PriceId).HasColumnName("price_id");
    entity.Property(e => e.SubscriptionId).HasColumnName("subscription_id");
    entity.Property(e => e.Status).HasColumnName("status");
    entity.Property(e => e.CurrentPeriodEndUtc).HasColumnName("current_period_end_utc");
    entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
    entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
    entity.HasIndex(e => e.UserId).HasDatabaseName("ix_user_stripe_subscriptions_user_id");
    entity.HasIndex(e => e.SubscriptionId).HasDatabaseName("ix_user_stripe_subscriptions_subscription_id");
});

            // 💬 ChatHistory Table Configuration
            modelBuilder.Entity<ChatHistory>(entity =>
            {
                entity.ToTable("chathistory");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasColumnName("Id");
                entity.Property(e => e.UserId).HasColumnName("UserId").IsRequired();
                entity.Property(e => e.Message).HasColumnName("Message").IsRequired();
                entity.Property(e => e.Reply).HasColumnName("Reply");
                entity.Property(e => e.Timestamp).HasColumnName("Timestamp").HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.Property(e => e.Title).HasColumnName("Title");
                entity.Property(e => e.ConversationId).HasColumnName("ConversationId");
                entity.Property(e => e.PromptTokens).HasColumnName("PromptTokens");
                entity.Property(e => e.CompletionTokens).HasColumnName("CompletionTokens");
                entity.Property(e => e.Cost).HasColumnName("Cost");
                entity.Property(e => e.Metadata).HasColumnName("Metadata").HasColumnType("TEXT");
                entity.HasIndex(e => e.UserId).HasDatabaseName("ix_chathistory_user_id");
                entity.HasIndex(e => e.Timestamp).HasDatabaseName("ix_chathistory_timestamp");
            });

            // ============================================================================
            // FACTORY ENTITY CONFIGURATIONS (Project Generation Pipeline)
            // ============================================================================

            // 🏭 ProjectIntents - stores user prompts for project creation
            modelBuilder.Entity<ProjectIntentEntity>(entity =>
            {
                entity.ToTable("project_intents");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasColumnName("id").HasMaxLength(36);
                entity.Property(e => e.UserId).HasColumnName("user_id").HasMaxLength(36);
                entity.Property(e => e.Prompt).HasColumnName("prompt").IsRequired();
                entity.Property(e => e.NormalizedPrompt).HasColumnName("normalized_prompt").IsRequired();
                entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.HasIndex(e => e.UserId).HasDatabaseName("ix_project_intents_user_id");
            });

            // 🏭 FeatureGraphs - stores generated feature graphs
            modelBuilder.Entity<FeatureGraphEntity>(entity =>
            {
                entity.ToTable("feature_graphs");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasColumnName("id").HasMaxLength(36);
                entity.Property(e => e.IntentId).HasColumnName("intent_id").HasMaxLength(36).IsRequired();
                entity.Property(e => e.ProjectName).HasColumnName("project_name").HasMaxLength(128).IsRequired();
                entity.Property(e => e.Language).HasColumnName("language").HasMaxLength(32).IsRequired();
                entity.Property(e => e.ProjectKind).HasColumnName("project_kind").HasMaxLength(32).IsRequired();
                entity.Property(e => e.GraphJson).HasColumnName("graph_json").IsRequired();
                entity.Property(e => e.IsValid).HasColumnName("is_valid");
                entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.HasIndex(e => e.IntentId).HasDatabaseName("ix_feature_graphs_intent_id");
            });

            // 🏭 GeneratedProjects - stores generated project metadata
            modelBuilder.Entity<GeneratedProjectEntity>(entity =>
            {
                entity.ToTable("generated_projects");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasColumnName("id").HasMaxLength(36);
                entity.Property(e => e.IntentId).HasColumnName("intent_id").HasMaxLength(36).IsRequired();
                entity.Property(e => e.ProjectName).HasColumnName("project_name").HasMaxLength(128).IsRequired();
                entity.Property(e => e.ZipHash).HasColumnName("zip_hash").HasMaxLength(64).IsRequired();
                entity.Property(e => e.FileCount).HasColumnName("file_count");
                entity.Property(e => e.BuildPassed).HasColumnName("build_passed");
                entity.Property(e => e.BuildLog).HasColumnName("build_log");
                entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.HasIndex(e => e.IntentId).HasDatabaseName("ix_generated_projects_intent_id");
            });

            // 🏭 LlmCalls - caches LLM calls for reuse
            modelBuilder.Entity<LlmCallEntity>(entity =>
            {
                entity.ToTable("llm_calls");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasColumnName("id").HasMaxLength(36);
                entity.Property(e => e.IntentId).HasColumnName("intent_id").HasMaxLength(36).IsRequired();
                entity.Property(e => e.PassName).HasColumnName("pass_name").HasMaxLength(32).IsRequired();
                entity.Property(e => e.PromptHash).HasColumnName("prompt_hash").HasMaxLength(64).IsRequired();
                entity.Property(e => e.InputPrompt).HasColumnName("input_prompt").IsRequired();
                entity.Property(e => e.OutputText).HasColumnName("output_text");
                entity.Property(e => e.IsValid).HasColumnName("is_valid");
                entity.Property(e => e.ErrorMessage).HasColumnName("error_message");
                entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.HasIndex(e => new { e.PromptHash, e.PassName }).HasDatabaseName("ix_llm_calls_hash_pass");
            });

            // 🏭 BuildRepairs - stores build repair attempts
            modelBuilder.Entity<BuildRepairEntity>(entity =>
            {
                entity.ToTable("build_repairs");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasColumnName("id").HasMaxLength(36);
                entity.Property(e => e.ProjectId).HasColumnName("project_id").HasMaxLength(36).IsRequired();
                entity.Property(e => e.Attempt).HasColumnName("attempt");
                entity.Property(e => e.ErrorLog).HasColumnName("error_log").IsRequired();
                entity.Property(e => e.PatchDiff).HasColumnName("patch_diff");
                entity.Property(e => e.Success).HasColumnName("success");
                entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.HasIndex(e => e.ProjectId).HasDatabaseName("ix_build_repairs_project_id");
            });

            // 🏭 CodeKnowledge - stores learned code patterns
            modelBuilder.Entity<CodeKnowledgeEntity>(entity =>
            {
                entity.ToTable("code_knowledge");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasColumnName("id").HasMaxLength(36);
                entity.Property(e => e.Language).HasColumnName("language").HasMaxLength(32).IsRequired();
                entity.Property(e => e.Pattern).HasColumnName("pattern").HasMaxLength(64).IsRequired();
                entity.Property(e => e.IssueSignature).HasColumnName("issue_signature").HasMaxLength(255).IsRequired();
                entity.Property(e => e.FixDescription).HasColumnName("fix_description").IsRequired();
                entity.Property(e => e.Confidence).HasColumnName("confidence");
                entity.Property(e => e.SourceProjectId).HasColumnName("source_project_id").HasMaxLength(36);
                entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.HasIndex(e => new { e.Language, e.Pattern }).HasDatabaseName("ix_code_knowledge_lang_pattern");
            });

            // 🏭 FeatureTemplates - stores reusable feature templates
            modelBuilder.Entity<FeatureTemplateEntity>(entity =>
            {
                entity.ToTable("feature_templates");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasColumnName("id").HasMaxLength(36);
                entity.Property(e => e.Language).HasColumnName("language").HasMaxLength(32).IsRequired();
                entity.Property(e => e.ProjectKind).HasColumnName("project_kind").HasMaxLength(32).IsRequired();
                entity.Property(e => e.Pattern).HasColumnName("pattern").HasMaxLength(64).IsRequired();
                entity.Property(e => e.TemplateGraph).HasColumnName("template_graph").IsRequired();
                entity.Property(e => e.Confidence).HasColumnName("confidence");
                entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.HasIndex(e => new { e.Language, e.ProjectKind }).HasDatabaseName("ix_feature_templates_lang_kind");
            });

            // ============================================================================
            // REFERRAL SYSTEM CONFIGURATIONS
            // ============================================================================

            // 🎁 ReferralSettings - singleton table for admin-configurable rewards
            modelBuilder.Entity<ReferralSettings>(entity =>
            {
                entity.ToTable("referral_settings");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasColumnName("id");
                entity.Property(e => e.ReferrerCredits).HasColumnName("referrer_credits");
                entity.Property(e => e.RefereeCredits).HasColumnName("referee_credits");
                entity.Property(e => e.IsEnabled).HasColumnName("is_enabled");
                entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            });

            // 🎁 ReferralTransactions - tracks each referral
            modelBuilder.Entity<ReferralTransaction>(entity =>
            {
                entity.ToTable("referral_transactions");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasColumnName("id");
                entity.Property(e => e.ReferrerId).HasColumnName("referrer_id");
                entity.Property(e => e.RefereeId).HasColumnName("referee_id");
                entity.Property(e => e.ReferralCode).HasColumnName("referral_code").HasMaxLength(20);
                entity.Property(e => e.ReferrerCreditsAwarded).HasColumnName("referrer_credits_awarded");
                entity.Property(e => e.RefereeCreditsAwarded).HasColumnName("referee_credits_awarded");
                entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.HasIndex(e => e.ReferrerId).HasDatabaseName("ix_referral_transactions_referrer_id");
                entity.HasIndex(e => e.RefereeId).HasDatabaseName("ix_referral_transactions_referee_id");
            });

            // ============================================================================
            // CREDIT SYSTEM SETTINGS CONFIGURATION
            // ============================================================================

            // 💳 CreditSystemSettings - singleton table for admin-configurable credit costs
            modelBuilder.Entity<CreditSystemSettings>(entity =>
            {
                entity.ToTable("credit_system_settings");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasColumnName("id");
                entity.Property(e => e.FreeDailyCredits).HasColumnName("free_daily_credits");
                entity.Property(e => e.DailyResetHourUtc).HasColumnName("daily_reset_hour_utc");
                entity.Property(e => e.NewUserCredits).HasColumnName("new_user_credits");
                entity.Property(e => e.CostPerMessage).HasColumnName("cost_per_message");
                entity.Property(e => e.CostPerToken).HasColumnName("cost_per_token");
                entity.Property(e => e.ProjectCreationBaseCost).HasColumnName("project_creation_base_cost");
                entity.Property(e => e.CodeAnalysisCost).HasColumnName("code_analysis_cost");
                entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            });

            // ============================================================================
            // PURCHASE-BASED REFERRAL REWARDS CONFIGURATIONS
            // ============================================================================

            // 💰 PurchaseReferralSettings - per-plan reward settings
            modelBuilder.Entity<PurchaseReferralSettings>(entity =>
            {
                entity.ToTable("purchase_referral_settings");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasColumnName("id");
                entity.Property(e => e.PlanId).HasColumnName("plan_id");
                entity.Property(e => e.ReferrerRewardCredits).HasColumnName("referrer_reward_credits");
                entity.Property(e => e.RefereeRewardCredits).HasColumnName("referee_reward_credits");
                entity.Property(e => e.OwnerPurchaseRewardCredits).HasColumnName("owner_purchase_reward_credits");
                entity.Property(e => e.IsEnabled).HasColumnName("is_enabled");
                entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.HasIndex(e => e.PlanId).HasDatabaseName("ix_purchase_referral_settings_plan_id").IsUnique();
            });

            // 💰 PurchaseReferralTransactions - tracks purchase-based rewards
            modelBuilder.Entity<PurchaseReferralTransaction>(entity =>
            {
                entity.ToTable("purchase_referral_transactions");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasColumnName("id");
                entity.Property(e => e.AuditLogId).HasColumnName("audit_log_id").HasMaxLength(36);
                entity.Property(e => e.PurchaserId).HasColumnName("purchaser_id");
                entity.Property(e => e.BeneficiaryId).HasColumnName("beneficiary_id");
                entity.Property(e => e.PlanId).HasColumnName("plan_id");
                entity.Property(e => e.RewardType).HasColumnName("reward_type").HasMaxLength(32);
                entity.Property(e => e.CreditsAwarded).HasColumnName("credits_awarded");
                entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.HasIndex(e => e.PurchaserId).HasDatabaseName("ix_purchase_referral_tx_purchaser");
                entity.HasIndex(e => e.BeneficiaryId).HasDatabaseName("ix_purchase_referral_tx_beneficiary");
                entity.HasIndex(e => e.AuditLogId).HasDatabaseName("ix_purchase_referral_tx_audit");
            });

            // 🔐 CreditAuditLog - security audit trail for all credit operations
            modelBuilder.Entity<CreditAuditLog>(entity =>
            {
                entity.ToTable("credit_audit_log");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasColumnName("id").HasMaxLength(36);
                entity.Property(e => e.UserId).HasColumnName("user_id");
                entity.Property(e => e.OperationType).HasColumnName("operation_type").HasMaxLength(64);
                entity.Property(e => e.CreditsAmount).HasColumnName("credits_amount");
                entity.Property(e => e.CreditsBefore).HasColumnName("credits_before");
                entity.Property(e => e.CreditsAfter).HasColumnName("credits_after");
                entity.Property(e => e.SourceType).HasColumnName("source_type").HasMaxLength(64);
                entity.Property(e => e.SourceReferenceId).HasColumnName("source_reference_id").HasMaxLength(128);
                entity.Property(e => e.RelatedUserId).HasColumnName("related_user_id");
                entity.Property(e => e.SecurityHash).HasColumnName("security_hash").HasMaxLength(128);
                entity.Property(e => e.IpAddress).HasColumnName("ip_address").HasMaxLength(45);
                entity.Property(e => e.UserAgent).HasColumnName("user_agent").HasMaxLength(512);
                entity.Property(e => e.IsValidated).HasColumnName("is_validated");
                entity.Property(e => e.ValidatedAt).HasColumnName("validated_at");
                entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.HasIndex(e => e.UserId).HasDatabaseName("ix_credit_audit_log_user_id");
                entity.HasIndex(e => e.OperationType).HasDatabaseName("ix_credit_audit_log_operation_type");
                entity.HasIndex(e => e.SecurityHash).HasDatabaseName("ix_credit_audit_log_security_hash");
                entity.HasIndex(e => e.CreatedAt).HasDatabaseName("ix_credit_audit_log_created_at");
            });

            // 🔐 CreditSecurityTokens - anti-replay attack tokens
            modelBuilder.Entity<CreditSecurityToken>(entity =>
            {
                entity.ToTable("credit_security_tokens");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasColumnName("id").HasMaxLength(36);
                entity.Property(e => e.UserId).HasColumnName("user_id");
                entity.Property(e => e.TokenHash).HasColumnName("token_hash").HasMaxLength(128);
                entity.Property(e => e.OperationType).HasColumnName("operation_type").HasMaxLength(64);
                entity.Property(e => e.IsUsed).HasColumnName("is_used");
                entity.Property(e => e.ExpiresAt).HasColumnName("expires_at");
                entity.Property(e => e.UsedAt).HasColumnName("used_at");
                entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.HasIndex(e => e.UserId).HasDatabaseName("ix_credit_security_tokens_user");
                entity.HasIndex(e => e.TokenHash).HasDatabaseName("ix_credit_security_tokens_hash");
                entity.HasIndex(e => e.ExpiresAt).HasDatabaseName("ix_credit_security_tokens_expires");
            });

            // ============================================================================
            // DEAD LETTER QUEUE CONFIGURATION
            // ============================================================================

            modelBuilder.Entity<LittleHelperAI.Shared.Models.DeadLetterMessage>(entity =>
            {
                entity.ToTable("dead_letter_messages");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasColumnName("id").HasMaxLength(36);
                entity.Property(e => e.UserId).HasColumnName("user_id");
                entity.Property(e => e.ChatId).HasColumnName("chat_id");
                entity.Property(e => e.ConversationId).HasColumnName("conversation_id").HasMaxLength(36);
                entity.Property(e => e.RequestPayload).HasColumnName("request_payload").IsRequired();
                entity.Property(e => e.ErrorMessage).HasColumnName("error_message").HasMaxLength(2048).IsRequired();
                entity.Property(e => e.ErrorType).HasColumnName("error_type").HasMaxLength(256).IsRequired();
                entity.Property(e => e.StackTrace).HasColumnName("stack_trace");
                entity.Property(e => e.RetryCount).HasColumnName("retry_count").HasDefaultValue(0);
                entity.Property(e => e.MaxRetries).HasColumnName("max_retries").HasDefaultValue(3);
                entity.Property(e => e.Status).HasColumnName("status").HasMaxLength(32)
                      .HasConversion<string>().HasDefaultValue(LittleHelperAI.Shared.Models.DeadLetterStatus.Pending);
                entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.Property(e => e.LastRetryAt).HasColumnName("last_retry_at");
                entity.Property(e => e.ResolvedAt).HasColumnName("resolved_at");
                entity.Property(e => e.ResolutionNotes).HasColumnName("resolution_notes");
                entity.Property(e => e.Metadata).HasColumnName("metadata");
                entity.HasIndex(e => e.UserId).HasDatabaseName("ix_dead_letter_user_id");
                entity.HasIndex(e => e.Status).HasDatabaseName("ix_dead_letter_status");
                entity.HasIndex(e => e.CreatedAt).HasDatabaseName("ix_dead_letter_created_at");
            });
        }
    }
}
