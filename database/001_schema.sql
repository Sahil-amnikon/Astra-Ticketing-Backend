SET XACT_ABORT ON;
GO
CREATE FUNCTION dbo.TextLength(@value nvarchar(max)) RETURNS bigint AS
BEGIN RETURN LEN(@value + N'.') - 1; END;
GO
CREATE FUNCTION dbo.JsonArrayCount(@value nvarchar(max)) RETURNS int AS
BEGIN
 IF ISJSON(@value, ARRAY) <> 1 RETURN -1;
 RETURN (SELECT COUNT(*) FROM OPENJSON(@value));
END;
GO
CREATE FUNCTION dbo.JsonContains(@value nvarchar(max), @item nvarchar(450)) RETURNS bit AS
BEGIN
 IF ISJSON(@value, ARRAY) <> 1 RETURN 0;
 IF EXISTS(SELECT 1 FROM OPENJSON(@value) WHERE [type]=1 AND [value]=@item) RETURN 1;
 RETURN 0;
END;
GO
CREATE FUNCTION dbo.JsonSubset(@value nvarchar(max), @allowed nvarchar(max)) RETURNS bit AS
BEGIN
 IF ISJSON(@value, ARRAY) <> 1 OR ISJSON(@allowed, ARRAY) <> 1 RETURN 0;
 IF EXISTS(SELECT 1 FROM OPENJSON(@value) v WHERE v.[type]<>1 OR NOT EXISTS(SELECT 1 FROM OPENJSON(@allowed) a WHERE a.[value]=v.[value])) RETURN 0;
 RETURN 1;
END;
GO
CREATE SEQUENCE dbo.problem_number_seq AS bigint START WITH 1 INCREMENT BY 1;
CREATE SEQUENCE dbo.change_number_seq AS bigint START WITH 1 INCREMENT BY 1;
GO
CREATE TABLE dbo.[access_groups](
  [id] uniqueidentifier DEFAULT NEWID() NOT NULL,
  [name] nvarchar(200) NOT NULL,
  [description] nvarchar(max) DEFAULT N'' NOT NULL,
  [kind] nvarchar(450) NOT NULL,
  [client_id] uniqueidentifier NULL,
  [permissions] nvarchar(max) DEFAULT N'[]' NOT NULL CONSTRAINT [access_groups_permissions_json] CHECK (ISJSON([permissions], ARRAY)=1),
  [active] bit DEFAULT 1 NOT NULL,
  [revision] int DEFAULT 1 NOT NULL,
  [created_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  [updated_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  CONSTRAINT [access_groups_pkey] PRIMARY KEY ([id])
);
GO
CREATE TABLE dbo.[app_schema_state](
  [id] smallint NOT NULL,
  [version] int NOT NULL,
  [created_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  CONSTRAINT [app_schema_state_pkey] PRIMARY KEY ([id])
);
GO
CREATE TABLE dbo.[assets](
  [id] uniqueidentifier DEFAULT NEWID() NOT NULL,
  [client_id] uniqueidentifier NOT NULL,
  [site_id] uniqueidentifier NULL,
  [name] nvarchar(max) NOT NULL,
  [asset_type] nvarchar(max) NOT NULL,
  [serial_number] nvarchar(450) NULL,
  [external_reference] nvarchar(max) NULL,
  [active] bit DEFAULT 1 NOT NULL,
  [created_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  [updated_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  CONSTRAINT [assets_pkey] PRIMARY KEY ([id])
);
GO
CREATE TABLE dbo.[audit_logs](
  [id] bigint IDENTITY(1,1) NOT NULL,
  [actor_user_id] uniqueidentifier NULL,
  [action] nvarchar(max) NOT NULL,
  [entity_type] nvarchar(450) NOT NULL,
  [entity_id] nvarchar(450) NULL,
  [details] nvarchar(max) DEFAULT N'{}' NOT NULL CONSTRAINT [audit_logs_details_json] CHECK (ISJSON([details], VALUE)=1),
  [request_id] nvarchar(max) NULL,
  [source_ip] nvarchar(49) NULL,
  [created_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  CONSTRAINT [audit_logs_pkey] PRIMARY KEY ([id])
);
GO
CREATE TABLE dbo.[cab_groups](
  [id] uniqueidentifier DEFAULT NEWID() NOT NULL,
  [name] nvarchar(200) NOT NULL,
  [quorum] int NOT NULL,
  [active] bit DEFAULT 1 NOT NULL,
  [created_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  CONSTRAINT [cab_groups_pkey] PRIMARY KEY ([id])
);
GO
CREATE TABLE dbo.[cab_members](
  [group_id] uniqueidentifier NOT NULL,
  [user_id] uniqueidentifier NOT NULL,
  CONSTRAINT [cab_members_pkey] PRIMARY KEY ([group_id],[user_id])
);
GO
CREATE TABLE dbo.[categories](
  [id] uniqueidentifier DEFAULT NEWID() NOT NULL,
  [parent_id] uniqueidentifier NULL,
  [name] nvarchar(200) NOT NULL,
  [description] nvarchar(max) NULL,
  [active] bit DEFAULT 1 NOT NULL,
  [created_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  [updated_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  CONSTRAINT [categories_pkey] PRIMARY KEY ([id])
);
GO
CREATE TABLE dbo.[change_approvals](
  [id] uniqueidentifier DEFAULT NEWID() NOT NULL,
  [change_id] uniqueidentifier NOT NULL,
  [approver_user_id] uniqueidentifier NULL,
  [department_id] uniqueidentifier NULL,
  [sequence] int DEFAULT 1 NOT NULL,
  [required] bit DEFAULT 1 NOT NULL,
  [status] nvarchar(max) DEFAULT N'PENDING' NOT NULL,
  [decision_by_user_id] uniqueidentifier NULL,
  [decision_note] nvarchar(max) NULL,
  [decided_at] datetimeoffset(7) NULL,
  [created_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  [revision] int DEFAULT 1 NOT NULL,
  [cab_group_id] uniqueidentifier NULL,
  CONSTRAINT [change_approvals_pkey] PRIMARY KEY ([id])
);
GO
CREATE TABLE dbo.[change_attachments](
  [id] uniqueidentifier DEFAULT NEWID() NOT NULL,
  [change_id] uniqueidentifier NOT NULL,
  [uploaded_by_user_id] uniqueidentifier NULL,
  [file_name] nvarchar(max) NOT NULL,
  [content_type] nvarchar(max) NOT NULL,
  [size_bytes] bigint NOT NULL,
  [sha256] nvarchar(max) NOT NULL,
  [content] varbinary(max) NOT NULL,
  [created_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  CONSTRAINT [change_attachments_pkey] PRIMARY KEY ([id])
);
GO
CREATE TABLE dbo.[change_blackouts](
  [id] uniqueidentifier DEFAULT NEWID() NOT NULL,
  [name] nvarchar(max) NOT NULL,
  [client_id] uniqueidentifier NULL,
  [site_id] uniqueidentifier NULL,
  [starts_at] datetimeoffset(7) NOT NULL,
  [ends_at] datetimeoffset(7) NOT NULL,
  [active] bit DEFAULT 1 NOT NULL,
  [created_by_user_id] uniqueidentifier NULL,
  CONSTRAINT [change_blackouts_pkey] PRIMARY KEY ([id])
);
GO
CREATE TABLE dbo.[change_communications](
  [id] uniqueidentifier DEFAULT NEWID() NOT NULL,
  [change_id] uniqueidentifier NOT NULL,
  [recipient_emails] nvarchar(max) NOT NULL CONSTRAINT [change_communications_recipient_emails_json] CHECK (ISJSON([recipient_emails], ARRAY)=1),
  [subject] nvarchar(max) NOT NULL,
  [body] nvarchar(max) NOT NULL,
  [include_client] bit DEFAULT 0 NOT NULL,
  [status] nvarchar(max) DEFAULT N'DRAFT' NOT NULL,
  [attempts] int DEFAULT 0 NOT NULL,
  [last_error_code] nvarchar(max) NULL,
  [sent_at] datetimeoffset(7) NULL,
  [created_by_user_id] uniqueidentifier NULL,
  [created_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  CONSTRAINT [change_communications_pkey] PRIMARY KEY ([id])
);
GO
CREATE TABLE dbo.[change_governance_policy](
  [id] smallint NOT NULL,
  [high_risk_cab] bit DEFAULT 1 NOT NULL,
  [minimum_approvals] int DEFAULT 1 NOT NULL,
  [review_required] bit DEFAULT 1 NOT NULL,
  CONSTRAINT [change_governance_policy_pkey] PRIMARY KEY ([id])
);
GO
CREATE TABLE dbo.[change_history](
  [id] bigint IDENTITY(1,1) NOT NULL,
  [change_id] uniqueidentifier NOT NULL,
  [actor_user_id] uniqueidentifier NULL,
  [event] nvarchar(max) NOT NULL,
  [details] nvarchar(max) DEFAULT N'{}' NOT NULL CONSTRAINT [change_history_details_json] CHECK (ISJSON([details], VALUE)=1),
  [created_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  CONSTRAINT [change_history_pkey] PRIMARY KEY ([id])
);
GO
CREATE TABLE dbo.[change_reviews](
  [id] uniqueidentifier DEFAULT NEWID() NOT NULL,
  [change_id] uniqueidentifier NOT NULL,
  [revision] int NOT NULL,
  [outcome] nvarchar(max) NOT NULL,
  [summary] nvarchar(max) NOT NULL,
  [follow_up] nvarchar(max) NOT NULL,
  [actor_user_id] uniqueidentifier NOT NULL,
  [created_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  CONSTRAINT [change_reviews_pkey] PRIMARY KEY ([id])
);
GO
CREATE TABLE dbo.[change_stakeholders](
  [id] uniqueidentifier DEFAULT NEWID() NOT NULL,
  [change_id] uniqueidentifier NOT NULL,
  [user_id] uniqueidentifier NULL,
  [external_name] nvarchar(max) NULL,
  [external_email] nvarchar(320) NULL,
  [stakeholder_role] nvarchar(max) DEFAULT N'STAKEHOLDER' NOT NULL,
  [added_by_user_id] uniqueidentifier NULL,
  [created_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  [contact_id] uniqueidentifier NULL,
  CONSTRAINT [change_stakeholders_pkey] PRIMARY KEY ([id])
);
GO
CREATE TABLE dbo.[change_tasks](
  [id] uniqueidentifier DEFAULT NEWID() NOT NULL,
  [change_id] uniqueidentifier NOT NULL,
  [department_id] uniqueidentifier NULL,
  [assigned_user_id] uniqueidentifier NULL,
  [title] nvarchar(max) NOT NULL,
  [instructions] nvarchar(max) DEFAULT N'' NOT NULL,
  [status] nvarchar(max) DEFAULT N'TODO' NOT NULL,
  [due_at] datetimeoffset(7) NULL,
  [completed_at] datetimeoffset(7) NULL,
  [update_note] nvarchar(max) NULL,
  [created_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  CONSTRAINT [change_tasks_pkey] PRIMARY KEY ([id])
);
GO
CREATE TABLE dbo.[change_teams](
  [change_id] uniqueidentifier NOT NULL,
  [department_id] uniqueidentifier NOT NULL,
  [role] nvarchar(max) NOT NULL,
  [status] nvarchar(max) DEFAULT N'PENDING' NOT NULL,
  CONSTRAINT [change_teams_pkey] PRIMARY KEY ([change_id],[department_id])
);
GO
CREATE TABLE dbo.[change_templates](
  [id] uniqueidentifier DEFAULT NEWID() NOT NULL,
  [name] nvarchar(200) NOT NULL,
  [category] nvarchar(max) NOT NULL,
  [description] nvarchar(max) NOT NULL,
  [implementation_plan] nvarchar(max) NOT NULL,
  [test_plan] nvarchar(max) NOT NULL,
  [rollback_plan] nvarchar(max) NOT NULL,
  [active] bit DEFAULT 1 NOT NULL,
  [created_by_user_id] uniqueidentifier NULL,
  [updated_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  CONSTRAINT [change_templates_pkey] PRIMARY KEY ([id])
);
GO
CREATE TABLE dbo.[change_ticket_links](
  [change_id] uniqueidentifier NOT NULL,
  [ticket_id] uniqueidentifier NOT NULL,
  [linked_by_user_id] uniqueidentifier NULL,
  CONSTRAINT [change_ticket_links_pkey] PRIMARY KEY ([change_id],[ticket_id])
);
GO
CREATE TABLE dbo.[change_updates](
  [id] uniqueidentifier DEFAULT NEWID() NOT NULL,
  [change_id] uniqueidentifier NOT NULL,
  [author_user_id] uniqueidentifier NULL,
  [update_type] nvarchar(max) NOT NULL,
  [body] nvarchar(max) NOT NULL,
  [created_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  CONSTRAINT [change_updates_pkey] PRIMARY KEY ([id])
);
GO
CREATE TABLE dbo.[changes](
  [id] uniqueidentifier DEFAULT NEWID() NOT NULL,
  [change_number] nvarchar(64) NOT NULL,
  [client_id] uniqueidentifier NOT NULL,
  [site_id] uniqueidentifier NULL,
  [department_id] uniqueidentifier NULL,
  [title] nvarchar(max) NOT NULL,
  [description] nvarchar(max) NOT NULL,
  [category] nvarchar(max) NOT NULL,
  [change_type] nvarchar(max) NOT NULL,
  [risk] nvarchar(max) NOT NULL,
  [status] nvarchar(450) DEFAULT N'DRAFT' NOT NULL,
  [impact] nvarchar(max) NOT NULL,
  [outage_expected] bit DEFAULT 0 NOT NULL,
  [downtime_minutes] int DEFAULT 0 NOT NULL,
  [reason] nvarchar(max) NOT NULL,
  [business_justification] nvarchar(max) NOT NULL,
  [implementation_plan] nvarchar(max) NOT NULL,
  [test_plan] nvarchar(max) NOT NULL,
  [rollback_plan] nvarchar(max) NOT NULL,
  [communication_plan] nvarchar(max) DEFAULT N'' NOT NULL,
  [client_notification_enabled] bit DEFAULT 0 NOT NULL,
  [owner_user_id] uniqueidentifier NOT NULL,
  [requested_by_user_id] uniqueidentifier NOT NULL,
  [planned_start] datetimeoffset(7) NULL,
  [planned_end] datetimeoffset(7) NULL,
  [actual_start] datetimeoffset(7) NULL,
  [actual_end] datetimeoffset(7) NULL,
  [created_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  [updated_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  [revision] int DEFAULT 1 NOT NULL,
  [cab_group_id] uniqueidentifier NULL,
  [cab_quorum] int DEFAULT 0 NOT NULL,
  [affected_resources] nvarchar(max) DEFAULT N'[]' NOT NULL CONSTRAINT [changes_affected_resources_json] CHECK (ISJSON([affected_resources], ARRAY)=1),
  CONSTRAINT [changes_pkey] PRIMARY KEY ([id])
);
GO
CREATE TABLE dbo.[client_email_domains](
  [id] uniqueidentifier DEFAULT NEWID() NOT NULL,
  [client_id] uniqueidentifier NOT NULL,
  [domain] nvarchar(253) NOT NULL,
  [site_id] uniqueidentifier NULL,
  [active] bit DEFAULT 1 NOT NULL,
  [revision] int DEFAULT 1 NOT NULL,
  [created_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  [updated_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  CONSTRAINT [client_email_domains_pkey] PRIMARY KEY ([id])
);
GO
CREATE TABLE dbo.[client_mail_routes](
  [address] nvarchar(450) NOT NULL,
  [client_id] uniqueidentifier NOT NULL,
  [active] bit DEFAULT 0 NOT NULL,
  [outbound_authorized] bit DEFAULT 0 NOT NULL,
  CONSTRAINT [client_mail_routes_pkey] PRIMARY KEY ([address])
);
GO
CREATE TABLE dbo.[client_ticket_sequences](
  [client_id] uniqueidentifier NOT NULL,
  [next_number] bigint DEFAULT '1' NOT NULL,
  CONSTRAINT [client_ticket_sequences_pkey] PRIMARY KEY ([client_id])
);
GO
CREATE TABLE dbo.[client_users](
  [client_id] uniqueidentifier NOT NULL,
  [user_id] uniqueidentifier NOT NULL,
  [created_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  CONSTRAINT [client_users_pkey] PRIMARY KEY ([client_id],[user_id])
);
GO
CREATE TABLE dbo.[clients](
  [id] uniqueidentifier DEFAULT NEWID() NOT NULL,
  [name] nvarchar(200) NOT NULL,
  [status] nvarchar(max) DEFAULT N'ACTIVE' NOT NULL,
  [primary_email] nvarchar(max) NULL,
  [primary_phone] nvarchar(max) NULL,
  [notes] nvarchar(max) NULL,
  [created_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  [updated_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  [organization_type] nvarchar(max) DEFAULT N'CLIENT' NOT NULL,
  [ticket_prefix] nvarchar(16) NOT NULL,
  CONSTRAINT [clients_pkey] PRIMARY KEY ([id])
);
GO
CREATE TABLE dbo.[contacts](
  [id] uniqueidentifier DEFAULT NEWID() NOT NULL,
  [client_id] uniqueidentifier NOT NULL,
  [user_id] uniqueidentifier NULL,
  [name] nvarchar(max) NOT NULL,
  [email] nvarchar(320) NULL,
  [phone] nvarchar(max) NULL,
  [active] bit DEFAULT 1 NOT NULL,
  [created_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  [updated_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  [site_id] uniqueidentifier NULL,
  CONSTRAINT [contacts_pkey] PRIMARY KEY ([id])
);
GO
CREATE TABLE dbo.[customer_group_members](
  [group_id] uniqueidentifier NOT NULL,
  [client_id] uniqueidentifier NOT NULL,
  [contact_id] uniqueidentifier NOT NULL,
  CONSTRAINT [customer_group_members_pkey] PRIMARY KEY ([group_id],[contact_id])
);
GO
CREATE TABLE dbo.[department_users](
  [department_id] uniqueidentifier NOT NULL,
  [user_id] uniqueidentifier NOT NULL,
  [expertise] nvarchar(max) NULL,
  [is_lead] bit DEFAULT 0 NOT NULL,
  CONSTRAINT [department_users_pkey] PRIMARY KEY ([department_id],[user_id])
);
GO
CREATE TABLE dbo.[departments](
  [id] uniqueidentifier DEFAULT NEWID() NOT NULL,
  [name] nvarchar(200) NOT NULL,
  [description] nvarchar(max) NULL,
  [active] bit DEFAULT 1 NOT NULL,
  [created_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  [updated_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  CONSTRAINT [departments_pkey] PRIMARY KEY ([id])
);
GO
CREATE TABLE dbo.[help_requests](
  [id] uniqueidentifier DEFAULT NEWID() NOT NULL,
  [ticket_id] uniqueidentifier NOT NULL,
  [requested_by_user_id] uniqueidentifier NOT NULL,
  [department_id] uniqueidentifier NULL,
  [requested_user_id] uniqueidentifier NULL,
  [expertise_needed] nvarchar(max) NOT NULL,
  [status] nvarchar(max) DEFAULT N'PENDING' NOT NULL,
  [responded_by_user_id] uniqueidentifier NULL,
  [response_note] nvarchar(max) NULL,
  [created_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  [responded_at] datetimeoffset(7) NULL,
  CONSTRAINT [help_requests_pkey] PRIMARY KEY ([id])
);
GO
CREATE TABLE dbo.[knowledge_articles](
  [id] uniqueidentifier DEFAULT NEWID() NOT NULL,
  [title] nvarchar(max) NOT NULL,
  [body] nvarchar(max) NOT NULL,
  [category_id] uniqueidentifier NULL,
  [client_id] uniqueidentifier NULL,
  [visibility] nvarchar(max) NOT NULL,
  [status] nvarchar(max) NOT NULL,
  [author_user_id] uniqueidentifier NOT NULL,
  [revision] int DEFAULT 1 NOT NULL,
  [updated_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  [created_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  CONSTRAINT [knowledge_articles_pkey] PRIMARY KEY ([id])
);
GO
CREATE TABLE dbo.[mail_attachments](
  [id] uniqueidentifier DEFAULT NEWID() NOT NULL,
  [message_id] uniqueidentifier NOT NULL,
  [file_name] nvarchar(max) NOT NULL,
  [content_type] nvarchar(max) NOT NULL,
  [content] varbinary(max) NOT NULL,
  [size_bytes] int NOT NULL,
  [created_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  CONSTRAINT [mail_attachments_pkey] PRIMARY KEY ([id])
);
GO
CREATE TABLE dbo.[mail_messages](
  [id] uniqueidentifier DEFAULT NEWID() NOT NULL,
  [direction] nvarchar(450) NOT NULL,
  [channel] nvarchar(max) NOT NULL,
  [ticket_id] uniqueidentifier NULL,
  [change_id] uniqueidentifier NULL,
  [comment_id] uniqueidentifier NULL,
  [communication_id] uniqueidentifier NULL,
  [mailbox] nvarchar(320) NOT NULL,
  [message_id] nvarchar(450) NOT NULL,
  [in_reply_to] nvarchar(max) NULL,
  [reference_ids] nvarchar(max) DEFAULT N'[]' NOT NULL CONSTRAINT [mail_messages_reference_ids_json] CHECK (ISJSON([reference_ids], ARRAY)=1),
  [sender_address] nvarchar(max) NOT NULL,
  [recipient_addresses] nvarchar(max) NOT NULL CONSTRAINT [mail_messages_recipient_addresses_json] CHECK (ISJSON([recipient_addresses], ARRAY)=1),
  [subject] nvarchar(max) NOT NULL,
  [body] nvarchar(max) NOT NULL,
  [state] nvarchar(450) NOT NULL,
  [attempts] int DEFAULT 0 NOT NULL,
  [next_attempt_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  [lease_until] datetimeoffset(7) NULL,
  [last_error_code] nvarchar(max) NULL,
  [processed_at] datetimeoffset(7) NULL,
  [created_by_user_id] uniqueidentifier NULL,
  [created_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  [contacted_address] nvarchar(max) NULL,
  CONSTRAINT [mail_messages_pkey] PRIMARY KEY ([id])
);
GO
CREATE TABLE dbo.[mail_settings](
  [id] smallint NOT NULL,
  [enabled] bit DEFAULT 0 NOT NULL,
  [smtp_host] nvarchar(max) DEFAULT N'mail.smtp2go.com' NOT NULL,
  [smtp_port] int DEFAULT 587 NOT NULL,
  [tls_mode] nvarchar(max) DEFAULT N'STARTTLS' NOT NULL,
  [smtp_username] nvarchar(max) DEFAULT N'' NOT NULL,
  [password_ciphertext] nvarchar(max) NULL,
  [ticket_address] nvarchar(max) DEFAULT N'it@amnikontechnologies.com' NOT NULL,
  [change_address] nvarchar(max) DEFAULT N'change@amnikontechnologies.com' NOT NULL,
  [inbound_provider] nvarchar(max) DEFAULT N'DISABLED' NOT NULL,
  [inbound_since] datetimeoffset(7) NULL,
  [updated_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  CONSTRAINT [mail_settings_pkey] PRIMARY KEY ([id])
);
GO
CREATE TABLE dbo.[mail_sync_state](
  [mailbox] nvarchar(320) NOT NULL,
  [cursor] nvarchar(max) NULL,
  [last_sync_at] datetimeoffset(7) NULL,
  [last_error_code] nvarchar(max) NULL,
  CONSTRAINT [mail_sync_state_pkey] PRIMARY KEY ([mailbox])
);
GO
CREATE TABLE dbo.[notification_preferences](
  [user_id] uniqueidentifier NOT NULL,
  [ticket_assigned] bit DEFAULT 1 NOT NULL,
  [ticket_transferred] bit DEFAULT 1 NOT NULL,
  [public_reply] bit DEFAULT 1 NOT NULL,
  [sla_at_risk] bit DEFAULT 1 NOT NULL,
  [sla_breached] bit DEFAULT 1 NOT NULL,
  [ticket_unattended] bit DEFAULT 1 NOT NULL,
  [updated_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  CONSTRAINT [notification_preferences_pkey] PRIMARY KEY ([user_id])
);
GO
CREATE TABLE dbo.[notifications](
  [id] uniqueidentifier DEFAULT NEWID() NOT NULL,
  [user_id] uniqueidentifier NOT NULL,
  [ticket_id] uniqueidentifier NULL,
  [type] nvarchar(max) NOT NULL,
  [title] nvarchar(max) NOT NULL,
  [body] nvarchar(max) NOT NULL,
  [event_key] nvarchar(450) NOT NULL,
  [read_at] datetimeoffset(7) NULL,
  [created_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  [change_id] uniqueidentifier NULL,
  CONSTRAINT [notifications_pkey] PRIMARY KEY ([id])
);
GO
CREATE TABLE dbo.[permissions](
  [code] nvarchar(64) NOT NULL,
  [name] nvarchar(max) NOT NULL,
  [description] nvarchar(max) NOT NULL,
  CONSTRAINT [permissions_pkey] PRIMARY KEY ([code])
);
GO
CREATE TABLE dbo.[portal_ticket_submissions](
  [request_id] uniqueidentifier NOT NULL,
  [domain_rule_id] uniqueidentifier NOT NULL,
  [client_id] uniqueidentifier NOT NULL,
  [requester_email] nvarchar(max) NOT NULL,
  [ticket_id] uniqueidentifier NOT NULL,
  [created_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  CONSTRAINT [portal_ticket_submissions_pkey] PRIMARY KEY ([request_id])
);
GO
CREATE TABLE dbo.[problem_history](
  [id] uniqueidentifier DEFAULT NEWID() NOT NULL,
  [problem_id] uniqueidentifier NOT NULL,
  [actor_user_id] uniqueidentifier NOT NULL,
  [action] nvarchar(max) NOT NULL,
  [details] nvarchar(max) DEFAULT N'{}' NOT NULL CONSTRAINT [problem_history_details_json] CHECK (ISJSON([details], VALUE)=1),
  [created_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  CONSTRAINT [problem_history_pkey] PRIMARY KEY ([id])
);
GO
CREATE TABLE dbo.[problem_tickets](
  [problem_id] uniqueidentifier NOT NULL,
  [ticket_id] uniqueidentifier NOT NULL,
  [client_id] uniqueidentifier NOT NULL,
  [linked_by_user_id] uniqueidentifier NOT NULL,
  [created_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  CONSTRAINT [problem_tickets_pkey] PRIMARY KEY ([problem_id],[ticket_id])
);
GO
CREATE TABLE dbo.[problems](
  [id] uniqueidentifier DEFAULT NEWID() NOT NULL,
  [number] bigint DEFAULT (NEXT VALUE FOR dbo.problem_number_seq) NOT NULL,
  [problem_number] AS (N'PRB-' + REPLICATE(N'0', CASE WHEN LEN(CONVERT(nvarchar(20),[number]))<8 THEN 8-LEN(CONVERT(nvarchar(20),[number])) ELSE 0 END) + CONVERT(nvarchar(20),[number])) PERSISTED,
  [client_id] uniqueidentifier NOT NULL,
  [site_id] uniqueidentifier NULL,
  [title] nvarchar(max) NOT NULL,
  [description] nvarchar(max) NOT NULL,
  [priority] nvarchar(max) DEFAULT N'P3_MEDIUM' NOT NULL,
  [status] nvarchar(450) DEFAULT N'OPEN' NOT NULL,
  [root_cause] nvarchar(max) DEFAULT N'' NOT NULL,
  [workaround] nvarchar(max) DEFAULT N'' NOT NULL,
  [resolution] nvarchar(max) DEFAULT N'' NOT NULL,
  [owner_user_id] uniqueidentifier NOT NULL,
  [created_by_user_id] uniqueidentifier NOT NULL,
  [revision] int DEFAULT 1 NOT NULL,
  [created_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  [updated_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  CONSTRAINT [problems_pkey] PRIMARY KEY ([id])
);
GO
CREATE TABLE dbo.[roles](
  [code] nvarchar(64) NOT NULL,
  [name] nvarchar(max) NOT NULL,
  [description] nvarchar(max) NOT NULL,
  [created_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  CONSTRAINT [roles_pkey] PRIMARY KEY ([code])
);
GO
CREATE TABLE dbo.[saved_ticket_views](
  [id] uniqueidentifier DEFAULT NEWID() NOT NULL,
  [user_id] uniqueidentifier NOT NULL,
  [name] nvarchar(200) NOT NULL,
  [query] nvarchar(max) DEFAULT N'{}' NOT NULL CONSTRAINT [saved_ticket_views_query_json] CHECK (ISJSON([query], VALUE)=1),
  [created_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  [updated_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  CONSTRAINT [saved_ticket_views_pkey] PRIMARY KEY ([id])
);
GO
CREATE TABLE dbo.[service_templates](
  [id] uniqueidentifier DEFAULT NEWID() NOT NULL,
  [name] nvarchar(max) NOT NULL,
  [description] nvarchar(max) NOT NULL,
  [category_id] uniqueidentifier NOT NULL,
  [client_id] uniqueidentifier NULL,
  [active] bit DEFAULT 1 NOT NULL,
  [instructions] nvarchar(max) NOT NULL,
  [created_by_user_id] uniqueidentifier NOT NULL,
  [updated_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  CONSTRAINT [service_templates_pkey] PRIMARY KEY ([id])
);
GO
CREATE TABLE dbo.[site_ticket_sequences](
  [site_id] uniqueidentifier NOT NULL,
  [next_number] bigint DEFAULT 1 NOT NULL,
  CONSTRAINT [site_ticket_sequences_pkey] PRIMARY KEY ([site_id])
);
GO
CREATE TABLE dbo.[sites](
  [id] uniqueidentifier DEFAULT NEWID() NOT NULL,
  [client_id] uniqueidentifier NOT NULL,
  [name] nvarchar(200) NOT NULL,
  [address] nvarchar(max) NULL,
  [active] bit DEFAULT 1 NOT NULL,
  [created_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  [updated_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  [ticket_prefix] nvarchar(16) NOT NULL,
  CONSTRAINT [sites_pkey] PRIMARY KEY ([id])
);
GO
CREATE TABLE dbo.[sla_calendars](
  [id] uniqueidentifier DEFAULT NEWID() NOT NULL,
  [name] nvarchar(200) NOT NULL,
  [timezone] nvarchar(max) NOT NULL,
  [working_days] nvarchar(max) NOT NULL CONSTRAINT [sla_calendars_working_days_json] CHECK (ISJSON([working_days], ARRAY)=1),
  [work_start] time(7) NOT NULL,
  [work_end] time(7) NOT NULL,
  [active] bit DEFAULT 1 NOT NULL,
  [updated_by_user_id] uniqueidentifier NULL,
  [created_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  [updated_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  CONSTRAINT [sla_calendars_pkey] PRIMARY KEY ([id])
);
GO
CREATE TABLE dbo.[sla_holidays](
  [id] uniqueidentifier DEFAULT NEWID() NOT NULL,
  [calendar_id] uniqueidentifier NOT NULL,
  [holiday_date] date NOT NULL,
  [name] nvarchar(max) NOT NULL,
  CONSTRAINT [sla_holidays_pkey] PRIMARY KEY ([id])
);
GO
CREATE TABLE dbo.[sla_policies](
  [id] uniqueidentifier DEFAULT NEWID() NOT NULL,
  [priority] nvarchar(450) NOT NULL,
  [response_minutes] int NOT NULL,
  [resolution_minutes] int NOT NULL,
  [warning_percent] int DEFAULT 75 NOT NULL,
  [active] bit DEFAULT 1 NOT NULL,
  [updated_by_user_id] uniqueidentifier NULL,
  [created_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  [updated_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  [calendar_id] uniqueidentifier NOT NULL,
  CONSTRAINT [sla_policies_pkey] PRIMARY KEY ([id])
);
GO
CREATE TABLE dbo.[staff_group_members](
  [group_id] uniqueidentifier NOT NULL,
  [user_id] uniqueidentifier NOT NULL,
  CONSTRAINT [staff_group_members_pkey] PRIMARY KEY ([group_id],[user_id])
);
GO
CREATE TABLE dbo.[tags](
  [id] uniqueidentifier DEFAULT NEWID() NOT NULL,
  [name] nvarchar(200) NOT NULL,
  [description] nvarchar(max) NULL,
  [active] bit DEFAULT 1 NOT NULL,
  [created_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  [updated_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  CONSTRAINT [tags_pkey] PRIMARY KEY ([id])
);
GO
CREATE TABLE dbo.[ticket_approvals](
  [id] uniqueidentifier DEFAULT NEWID() NOT NULL,
  [ticket_id] uniqueidentifier NOT NULL,
  [requested_by_user_id] uniqueidentifier NOT NULL,
  [decided_by_user_id] uniqueidentifier NULL,
  [status] nvarchar(450) DEFAULT N'PENDING' NOT NULL,
  [reason] nvarchar(max) NOT NULL,
  [decision_note] nvarchar(max) NULL,
  [created_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  [decided_at] datetimeoffset(7) NULL,
  CONSTRAINT [ticket_approvals_pkey] PRIMARY KEY ([id])
);
GO
CREATE TABLE dbo.[ticket_assignments](
  [id] bigint IDENTITY(1,1) NOT NULL,
  [ticket_id] uniqueidentifier NOT NULL,
  [previous_user_id] uniqueidentifier NULL,
  [assigned_user_id] uniqueidentifier NULL,
  [actor_user_id] uniqueidentifier NOT NULL,
  [reason] nvarchar(max) NULL,
  [created_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  CONSTRAINT [ticket_assignments_pkey] PRIMARY KEY ([id])
);
GO
CREATE TABLE dbo.[ticket_attachments](
  [id] uniqueidentifier DEFAULT NEWID() NOT NULL,
  [ticket_id] uniqueidentifier NOT NULL,
  [uploaded_by_user_id] uniqueidentifier NOT NULL,
  [file_name] nvarchar(max) NOT NULL,
  [content_type] nvarchar(max) NOT NULL,
  [size_bytes] bigint NOT NULL,
  [storage_key] nvarchar(450) NOT NULL,
  [sha256] nvarchar(max) NOT NULL,
  [content] varbinary(max) NOT NULL,
  [created_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  [visibility] nvarchar(max) DEFAULT N'INTERNAL' NOT NULL,
  CONSTRAINT [ticket_attachments_pkey] PRIMARY KEY ([id])
);
GO
CREATE TABLE dbo.[ticket_collaborators](
  [ticket_id] uniqueidentifier NOT NULL,
  [user_id] uniqueidentifier NOT NULL,
  [added_by_user_id] uniqueidentifier NULL,
  [reason] nvarchar(max) NOT NULL,
  [created_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  CONSTRAINT [ticket_collaborators_pkey] PRIMARY KEY ([ticket_id],[user_id])
);
GO
CREATE TABLE dbo.[ticket_comments](
  [id] uniqueidentifier DEFAULT NEWID() NOT NULL,
  [ticket_id] uniqueidentifier NOT NULL,
  [author_user_id] uniqueidentifier NOT NULL,
  [comment_type] nvarchar(max) NOT NULL,
  [body] nvarchar(max) NOT NULL,
  [created_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  CONSTRAINT [ticket_comments_pkey] PRIMARY KEY ([id])
);
GO
CREATE TABLE dbo.[ticket_history](
  [id] bigint IDENTITY(1,1) NOT NULL,
  [ticket_id] uniqueidentifier NOT NULL,
  [actor_user_id] uniqueidentifier NULL,
  [action] nvarchar(max) NOT NULL,
  [previous_value] nvarchar(max) NULL CONSTRAINT [ticket_history_previous_value_json] CHECK (ISJSON([previous_value], VALUE)=1),
  [new_value] nvarchar(max) NULL CONSTRAINT [ticket_history_new_value_json] CHECK (ISJSON([new_value], VALUE)=1),
  [reason] nvarchar(max) NULL,
  [created_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  CONSTRAINT [ticket_history_pkey] PRIMARY KEY ([id])
);
GO
CREATE TABLE dbo.[ticket_knowledge_links](
  [ticket_id] uniqueidentifier NOT NULL,
  [article_id] uniqueidentifier NOT NULL,
  [linked_by_user_id] uniqueidentifier NOT NULL,
  [created_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  CONSTRAINT [ticket_knowledge_links_pkey] PRIMARY KEY ([ticket_id],[article_id])
);
GO
CREATE TABLE dbo.[ticket_prefix_registry](
  [prefix] nvarchar(16) NOT NULL,
  [owner_kind] nvarchar(max) NOT NULL,
  [owner_id] uniqueidentifier NULL,
  CONSTRAINT [ticket_prefix_registry_pkey] PRIMARY KEY ([prefix])
);
GO
CREATE TABLE dbo.[ticket_sla](
  [id] uniqueidentifier DEFAULT NEWID() NOT NULL,
  [ticket_id] uniqueidentifier NOT NULL,
  [policy_id] uniqueidentifier NOT NULL,
  [target_priority] nvarchar(max) NOT NULL,
  [response_warning_at] datetimeoffset(7) NOT NULL,
  [response_due_at] datetimeoffset(7) NOT NULL,
  [resolution_warning_at] datetimeoffset(7) NOT NULL,
  [resolution_due_at] datetimeoffset(7) NOT NULL,
  [response_completed_at] datetimeoffset(7) NULL,
  [resolution_completed_at] datetimeoffset(7) NULL,
  [created_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  [updated_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  [calendar_id] uniqueidentifier NOT NULL,
  [paused_at] datetimeoffset(7) NULL,
  [response_warning_remaining_minutes] int NULL,
  [response_remaining_minutes] int NULL,
  [resolution_warning_remaining_minutes] int NULL,
  [resolution_remaining_minutes] int NULL,
  CONSTRAINT [ticket_sla_pkey] PRIMARY KEY ([id])
);
GO
CREATE TABLE dbo.[ticket_sources](
  [code] nvarchar(64) NOT NULL,
  [name] nvarchar(max) NOT NULL,
  [active] bit DEFAULT 1 NOT NULL,
  [sort_order] int DEFAULT 100 NOT NULL,
  [created_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  [updated_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  CONSTRAINT [ticket_sources_pkey] PRIMARY KEY ([code])
);
GO
CREATE TABLE dbo.[ticket_tags](
  [ticket_id] uniqueidentifier NOT NULL,
  [tag_id] uniqueidentifier NOT NULL,
  [added_by_user_id] uniqueidentifier NOT NULL,
  [created_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  CONSTRAINT [ticket_tags_pkey] PRIMARY KEY ([ticket_id],[tag_id])
);
GO
CREATE TABLE dbo.[tickets](
  [id] uniqueidentifier DEFAULT NEWID() NOT NULL,
  [ticket_number] nvarchar(64) NOT NULL,
  [client_id] uniqueidentifier NOT NULL,
  [requester_id] uniqueidentifier NOT NULL,
  [site_id] uniqueidentifier NULL,
  [category_id] uniqueidentifier NOT NULL,
  [assigned_user_id] uniqueidentifier NULL,
  [created_by_user_id] uniqueidentifier NULL,
  [subject] nvarchar(max) NOT NULL,
  [description] nvarchar(max) NOT NULL,
  [source] nvarchar(64) NOT NULL,
  [source_reference_id] nvarchar(max) NULL,
  [source_conversation_id] nvarchar(max) NULL,
  [source_message_id] nvarchar(max) NULL,
  [source_url] nvarchar(max) NULL,
  [external_thread_id] nvarchar(max) NULL,
  [status] nvarchar(450) DEFAULT N'NEW' NOT NULL,
  [priority] nvarchar(450) DEFAULT N'P3_MEDIUM' NOT NULL,
  [approval_status] nvarchar(max) DEFAULT N'NOT_REQUIRED' NOT NULL,
  [first_response_at] datetimeoffset(7) NULL,
  [resolved_at] datetimeoffset(7) NULL,
  [closed_at] datetimeoffset(7) NULL,
  [created_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  [updated_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  [department_id] uniqueidentifier NULL,
  [location_text] nvarchar(max) NULL,
  [email_reply_address] nvarchar(max) NULL,
  CONSTRAINT [tickets_pkey] PRIMARY KEY ([id])
);
GO
CREATE TABLE dbo.[user_permissions](
  [user_id] uniqueidentifier NOT NULL,
  [permission_code] nvarchar(64) NOT NULL,
  [granted_by] uniqueidentifier NULL,
  [created_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  CONSTRAINT [user_permissions_pkey] PRIMARY KEY ([user_id],[permission_code])
);
GO
CREATE TABLE dbo.[user_roles](
  [user_id] uniqueidentifier NOT NULL,
  [role_code] nvarchar(64) NOT NULL,
  [granted_by] uniqueidentifier NULL,
  [created_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  CONSTRAINT [user_roles_pkey] PRIMARY KEY ([user_id],[role_code])
);
GO
CREATE TABLE dbo.[user_sites](
  [user_id] uniqueidentifier NOT NULL,
  [site_id] uniqueidentifier NOT NULL,
  [created_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  CONSTRAINT [user_sites_pkey] PRIMARY KEY ([user_id],[site_id])
);
GO
CREATE TABLE dbo.[users](
  [id] uniqueidentifier DEFAULT NEWID() NOT NULL,
  [entra_object_id] uniqueidentifier NOT NULL,
  [entra_tenant_id] uniqueidentifier NOT NULL,
  [email] nvarchar(320) NOT NULL,
  [display_name] nvarchar(max) NOT NULL,
  [active] bit DEFAULT 1 NOT NULL,
  [last_login_at] datetimeoffset(7) NULL,
  [created_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  [updated_at] datetimeoffset(7) DEFAULT SYSUTCDATETIME() NOT NULL,
  [session_version] int DEFAULT 0 NOT NULL,
  [work_scope] nvarchar(max) DEFAULT N'CLIENT' NOT NULL,
  [change_access] nvarchar(max) DEFAULT N'NONE' NOT NULL,
  [supervisor_user_id] uniqueidentifier NULL,
  [access_expires_at] datetimeoffset(7) NULL,
  CONSTRAINT [users_pkey] PRIMARY KEY ([id])
);
GO
ALTER TABLE dbo.[access_groups] ADD CONSTRAINT [access_groups_check] CHECK (((([kind] = 'CUSTOMER') AND ([client_id] IS NOT NULL) AND (dbo.JsonSubset([permissions], N'["PORTAL_ORGANIZATION_VIEW", "PORTAL_ORGANIZATION_REPLY"]')=1)) OR (([kind] = 'STAFF') AND ([client_id] IS NULL) AND (dbo.JsonSubset([permissions], N'["TICKETS_TEAM_VIEW", "TICKETS_ASSIGN", "TICKETS_APPROVE", "CATEGORY_MANAGE", "SLA_MANAGE", "REPORTS_VIEW", "TAGS_MANAGE"]')=1))));
GO
ALTER TABLE dbo.[access_groups] ADD CONSTRAINT [access_groups_description_check] CHECK ((dbo.TextLength([description]) <= 500));
GO
CREATE UNIQUE INDEX [access_groups_id_client_id_key] ON dbo.[access_groups]([id],[client_id]);
GO
ALTER TABLE dbo.[access_groups] ADD CONSTRAINT [access_groups_kind_check] CHECK (([kind] IN ('CUSTOMER', 'STAFF')));
GO
ALTER TABLE dbo.[access_groups] ADD CONSTRAINT [access_groups_name_check] CHECK (((dbo.TextLength(TRIM([name])) >= 2) AND (dbo.TextLength(TRIM([name])) <= 120)));
GO
ALTER TABLE dbo.[access_groups] ADD CONSTRAINT [access_groups_permissions_check] CHECK (((NOT (dbo.JsonContains([permissions], 'PORTAL_ORGANIZATION_REPLY')=1)) OR (dbo.JsonContains([permissions], 'PORTAL_ORGANIZATION_VIEW')=1)));
GO
ALTER TABLE dbo.[app_schema_state] ADD CONSTRAINT [app_schema_state_id_check] CHECK (([id] = 1));
GO
ALTER TABLE dbo.[app_schema_state] ADD CONSTRAINT [app_schema_state_version_check] CHECK (([version] > 0));
GO
CREATE UNIQUE INDEX [cab_groups_name_key] ON dbo.[cab_groups]([name]);
GO
ALTER TABLE dbo.[cab_groups] ADD CONSTRAINT [cab_groups_quorum_check] CHECK ((([quorum] > 0) AND ([quorum] <= 50)));
GO
ALTER TABLE dbo.[change_approvals] ADD CONSTRAINT [change_approvals_check] CHECK ((([approver_user_id] IS NOT NULL) OR ([department_id] IS NOT NULL)));
GO
ALTER TABLE dbo.[change_approvals] ADD CONSTRAINT [change_approvals_status_check] CHECK (([status] IN ('PENDING', 'APPROVED', 'REJECTED', 'CHANGES_REQUESTED')));
GO
ALTER TABLE dbo.[change_attachments] ADD CONSTRAINT [change_attachments_size_bytes_check] CHECK ((([size_bytes] > 0) AND ([size_bytes] <= 5242880)));
GO
ALTER TABLE dbo.[change_blackouts] ADD CONSTRAINT [change_blackouts_check] CHECK (([ends_at] > [starts_at]));
GO
ALTER TABLE dbo.[change_blackouts] ADD CONSTRAINT [change_blackouts_check1] CHECK ((([site_id] IS NULL) OR ([client_id] IS NOT NULL)));
GO
ALTER TABLE dbo.[change_communications] ADD CONSTRAINT [change_communications_status_check] CHECK (([status] IN ('DRAFT', 'QUEUED', 'SENT', 'FAILED')));
GO
ALTER TABLE dbo.[change_governance_policy] ADD CONSTRAINT [change_governance_policy_id_check] CHECK (([id] = 1));
GO
ALTER TABLE dbo.[change_governance_policy] ADD CONSTRAINT [change_governance_policy_minimum_approvals_check] CHECK ((([minimum_approvals] >= 1) AND ([minimum_approvals] <= 10)));
GO
ALTER TABLE dbo.[change_reviews] ADD CONSTRAINT [change_reviews_outcome_check] CHECK (([outcome] IN ('SUCCESS', 'PARTIAL', 'FAILED', 'ROLLED_BACK')));
GO
ALTER TABLE dbo.[change_stakeholders] ADD CONSTRAINT [change_stakeholders_identity_check] CHECK (((([contact_id] IS NOT NULL) AND ([user_id] IS NULL) AND ([external_email] IS NULL)) OR (([contact_id] IS NULL) AND ([user_id] IS NOT NULL) AND ([external_email] IS NULL)) OR (([contact_id] IS NULL) AND ([user_id] IS NULL) AND ([external_email] IS NOT NULL))));
GO
ALTER TABLE dbo.[change_stakeholders] ADD CONSTRAINT [change_stakeholders_stakeholder_role_check] CHECK (([stakeholder_role] IN ('STAKEHOLDER', 'BUSINESS_CONTACT', 'TECHNICAL_CONTACT', 'OBSERVER')));
GO
ALTER TABLE dbo.[change_tasks] ADD CONSTRAINT [change_tasks_status_check] CHECK (([status] IN ('TODO', 'IN_PROGRESS', 'BLOCKED', 'COMPLETED', 'WAIVED')));
GO
ALTER TABLE dbo.[change_teams] ADD CONSTRAINT [change_teams_role_check] CHECK (([role] IN ('LEAD', 'IMPLEMENTER', 'REVIEWER')));
GO
ALTER TABLE dbo.[change_teams] ADD CONSTRAINT [change_teams_status_check] CHECK (([status] IN ('PENDING', 'ACTIVE', 'COMPLETED')));
GO
ALTER TABLE dbo.[change_templates] ADD CONSTRAINT [change_templates_category_check] CHECK (([category] IN ('SERVER', 'CLOUD', 'MICROSOFT_365', 'NETWORK', 'SECURITY', 'APPLICATION', 'OTHER')));
GO
CREATE UNIQUE INDEX [change_templates_name_key] ON dbo.[change_templates]([name]);
GO
ALTER TABLE dbo.[change_updates] ADD CONSTRAINT [change_updates_update_type_check] CHECK (([update_type] IN ('INTERNAL', 'IMPLEMENTATION', 'VALIDATION', 'CLIENT_COMMUNICATION')));
GO
ALTER TABLE dbo.[changes] ADD CONSTRAINT [changes_cab_quorum_check] CHECK (([cab_quorum] >= 0));
GO
ALTER TABLE dbo.[changes] ADD CONSTRAINT [changes_category_check] CHECK (([category] IN ('SERVER', 'CLOUD', 'MICROSOFT_365', 'NETWORK', 'SECURITY', 'APPLICATION', 'OTHER')));
GO
CREATE UNIQUE INDEX [changes_change_number_unique] ON dbo.[changes]([change_number]);
GO
ALTER TABLE dbo.[changes] ADD CONSTRAINT [changes_change_type_check] CHECK (([change_type] IN ('STANDARD', 'NORMAL', 'EMERGENCY')));
GO
ALTER TABLE dbo.[changes] ADD CONSTRAINT [changes_check] CHECK ((([planned_end] IS NULL) OR ([planned_start] IS NULL) OR ([planned_end] > [planned_start])));
GO
ALTER TABLE dbo.[changes] ADD CONSTRAINT [changes_downtime_minutes_check] CHECK (([downtime_minutes] >= 0));
GO
ALTER TABLE dbo.[changes] ADD CONSTRAINT [changes_revision_check] CHECK (([revision] > 0));
GO
ALTER TABLE dbo.[changes] ADD CONSTRAINT [changes_risk_check] CHECK (([risk] IN ('LOW', 'MEDIUM', 'HIGH', 'CRITICAL')));
GO
ALTER TABLE dbo.[changes] ADD CONSTRAINT [changes_status_check] CHECK (([status] IN ('DRAFT', 'SUBMITTED', 'ASSESSMENT', 'AWAITING_APPROVAL', 'APPROVED', 'SCHEDULED', 'IN_PROGRESS', 'VALIDATION', 'COMPLETED', 'CLOSED', 'REJECTED', 'CANCELLED', 'FAILED', 'ROLLED_BACK')));
GO
ALTER TABLE dbo.[client_email_domains] ADD CONSTRAINT [client_email_domains_domain_check] CHECK ((([domain] = lower(TRIM([domain]))) AND (REGEXP_LIKE([domain], '^[a-z0-9][a-z0-9.-]+[.][a-z0-9-]+$'))));
GO
CREATE UNIQUE INDEX [client_email_domains_domain_key] ON dbo.[client_email_domains]([domain]);
GO
CREATE UNIQUE INDEX [client_email_domains_id_client_id_key] ON dbo.[client_email_domains]([id],[client_id]);
GO
ALTER TABLE dbo.[client_mail_routes] ADD CONSTRAINT [client_mail_routes_address_check] CHECK ((([address] = lower(TRIM([address]))) AND ([address] LIKE '%@%')));
GO
ALTER TABLE dbo.[clients] ADD CONSTRAINT [clients_organization_type_check] CHECK (([organization_type] IN ('CLIENT', 'INTERNAL')));
GO
ALTER TABLE dbo.[clients] ADD CONSTRAINT [clients_status_check] CHECK (([status] IN ('ACTIVE', 'INACTIVE')));
GO
ALTER TABLE dbo.[clients] ADD CONSTRAINT [clients_ticket_prefix_format] CHECK ((REGEXP_LIKE([ticket_prefix], '^[A-Z][A-Z0-9]{1,7}$')));
GO
CREATE UNIQUE INDEX [clients_ticket_prefix_unique] ON dbo.[clients]([ticket_prefix]);
GO
CREATE UNIQUE INDEX [contacts_id_client_uq] ON dbo.[contacts]([id],[client_id]);
GO
CREATE UNIQUE INDEX [contacts_user_id_unique] ON dbo.[contacts]([user_id]) WHERE [user_id] IS NOT NULL;
GO
CREATE UNIQUE INDEX [departments_name_unique] ON dbo.[departments]([name]);
GO
ALTER TABLE dbo.[help_requests] ADD CONSTRAINT [help_requests_check] CHECK ((([department_id] IS NOT NULL) OR ([requested_user_id] IS NOT NULL)));
GO
ALTER TABLE dbo.[help_requests] ADD CONSTRAINT [help_requests_status_check] CHECK (([status] IN ('PENDING', 'ACCEPTED', 'DECLINED', 'COMPLETED')));
GO
ALTER TABLE dbo.[knowledge_articles] ADD CONSTRAINT [knowledge_articles_status_check] CHECK (([status] IN ('DRAFT', 'PUBLISHED', 'ARCHIVED')));
GO
ALTER TABLE dbo.[knowledge_articles] ADD CONSTRAINT [knowledge_articles_visibility_check] CHECK (([visibility] IN ('STAFF', 'CLIENT')));
GO
ALTER TABLE dbo.[mail_attachments] ADD CONSTRAINT [mail_attachments_size_bytes_check] CHECK ((([size_bytes] > 0) AND ([size_bytes] <= 5242880)));
GO
ALTER TABLE dbo.[mail_messages] ADD CONSTRAINT [mail_messages_channel_check] CHECK (([channel] IN ('TICKET', 'CHANGE', 'TEST')));
GO
ALTER TABLE dbo.[mail_messages] ADD CONSTRAINT [mail_messages_check] CHECK ((NOT (([ticket_id] IS NOT NULL) AND ([change_id] IS NOT NULL))));
GO
ALTER TABLE dbo.[mail_messages] ADD CONSTRAINT [mail_messages_direction_check] CHECK (([direction] IN ('INBOUND', 'OUTBOUND')));
GO
CREATE UNIQUE INDEX [mail_messages_mailbox_message_id_key] ON dbo.[mail_messages]([mailbox],[message_id]);
GO
ALTER TABLE dbo.[mail_messages] ADD CONSTRAINT [mail_messages_state_check] CHECK (([state] IN ('QUEUED', 'SENDING', 'SENT', 'FAILED', 'UNKNOWN', 'RECEIVED', 'REVIEW', 'IGNORED')));
GO
ALTER TABLE dbo.[mail_settings] ADD CONSTRAINT [mail_settings_id_check] CHECK (([id] = 1));
GO
ALTER TABLE dbo.[mail_settings] ADD CONSTRAINT [mail_settings_inbound_provider_check] CHECK (([inbound_provider] IN ('DISABLED', 'MICROSOFT_365')));
GO
ALTER TABLE dbo.[mail_settings] ADD CONSTRAINT [mail_settings_tls_mode_check] CHECK (([tls_mode] IN ('STARTTLS', 'TLS')));
GO
CREATE UNIQUE INDEX [notifications_event_key_unique] ON dbo.[notifications]([event_key]);
GO
ALTER TABLE dbo.[notifications] ADD CONSTRAINT [notifications_type_check] CHECK (([type] IN ('TICKET_ASSIGNED', 'TICKET_TRANSFERRED', 'PUBLIC_REPLY', 'SLA_AT_RISK', 'SLA_BREACHED', 'TICKET_UNATTENDED', 'CHANGE_APPROVAL_REQUESTED', 'CHANGE_TASK_ASSIGNED', 'CHANGE_STATUS_UPDATED', 'HELP_REQUESTED', 'HELP_ACCEPTED')));
GO
ALTER TABLE dbo.[portal_ticket_submissions] ADD CONSTRAINT [portal_ticket_submissions_requester_email_check] CHECK (([requester_email] = lower(TRIM([requester_email]))));
GO
CREATE UNIQUE INDEX [portal_ticket_submissions_ticket_id_key] ON dbo.[portal_ticket_submissions]([ticket_id]);
GO
ALTER TABLE dbo.[problems] ADD CONSTRAINT [problems_check] CHECK ((([status] NOT IN ('KNOWN_ERROR', 'RESOLVED', 'CLOSED')) OR (dbo.TextLength(TRIM([root_cause])) >= 3)));
GO
ALTER TABLE dbo.[problems] ADD CONSTRAINT [problems_check1] CHECK ((([status] <> 'KNOWN_ERROR') OR (dbo.TextLength(TRIM([workaround])) >= 3)));
GO
ALTER TABLE dbo.[problems] ADD CONSTRAINT [problems_check2] CHECK ((([status] NOT IN ('RESOLVED', 'CLOSED')) OR (dbo.TextLength(TRIM([resolution])) >= 3)));
GO
ALTER TABLE dbo.[problems] ADD CONSTRAINT [problems_description_check] CHECK (((dbo.TextLength(TRIM([description])) >= 3) AND (dbo.TextLength(TRIM([description])) <= 10000)));
GO
CREATE UNIQUE INDEX [problems_id_client_id_key] ON dbo.[problems]([id],[client_id]);
GO
CREATE UNIQUE INDEX [problems_number_key] ON dbo.[problems]([number]);
GO
ALTER TABLE dbo.[problems] ADD CONSTRAINT [problems_priority_check] CHECK (([priority] IN ('P1_CRITICAL', 'P2_HIGH', 'P3_MEDIUM', 'P4_LOW')));
GO
CREATE UNIQUE INDEX [problems_problem_number_key] ON dbo.[problems]([problem_number]);
GO
ALTER TABLE dbo.[problems] ADD CONSTRAINT [problems_status_check] CHECK (([status] IN ('OPEN', 'INVESTIGATING', 'KNOWN_ERROR', 'RESOLVED', 'CLOSED')));
GO
ALTER TABLE dbo.[problems] ADD CONSTRAINT [problems_title_check] CHECK (((dbo.TextLength(TRIM([title])) >= 3) AND (dbo.TextLength(TRIM([title])) <= 200)));
GO
ALTER TABLE dbo.[site_ticket_sequences] ADD CONSTRAINT [site_ticket_sequences_next_number_check] CHECK (([next_number] > 0));
GO
CREATE UNIQUE INDEX [sites_client_id_name_unique] ON dbo.[sites]([client_id],[name]);
GO
CREATE UNIQUE INDEX [sites_id_client_id_unique] ON dbo.[sites]([id],[client_id]);
GO
ALTER TABLE dbo.[sites] ADD CONSTRAINT [sites_ticket_prefix_format] CHECK ((REGEXP_LIKE([ticket_prefix], '^[A-Z][A-Z0-9]{1,15}$')));
GO
ALTER TABLE dbo.[sla_calendars] ADD CONSTRAINT [sla_calendars_check] CHECK (([work_end] > [work_start]));
GO
CREATE UNIQUE INDEX [sla_calendars_name_unique] ON dbo.[sla_calendars]([name]);
GO
ALTER TABLE dbo.[sla_calendars] ADD CONSTRAINT [sla_calendars_working_days_check] CHECK (((dbo.JsonArrayCount([working_days]) >= 1) AND (dbo.JsonArrayCount([working_days]) <= 7)));
GO
CREATE UNIQUE INDEX [sla_holidays_calendar_id_holiday_date_unique] ON dbo.[sla_holidays]([calendar_id],[holiday_date]);
GO
ALTER TABLE dbo.[sla_policies] ADD CONSTRAINT [sla_policies_check] CHECK (([resolution_minutes] >= [response_minutes]));
GO
ALTER TABLE dbo.[sla_policies] ADD CONSTRAINT [sla_policies_priority_check] CHECK (([priority] IN ('P1_CRITICAL', 'P2_HIGH', 'P3_MEDIUM', 'P4_LOW')));
GO
CREATE UNIQUE INDEX [sla_policies_priority_unique] ON dbo.[sla_policies]([priority]);
GO
ALTER TABLE dbo.[sla_policies] ADD CONSTRAINT [sla_policies_resolution_minutes_check] CHECK ((([resolution_minutes] >= 1) AND ([resolution_minutes] <= 525600)));
GO
ALTER TABLE dbo.[sla_policies] ADD CONSTRAINT [sla_policies_response_minutes_check] CHECK ((([response_minutes] >= 1) AND ([response_minutes] <= 525600)));
GO
ALTER TABLE dbo.[sla_policies] ADD CONSTRAINT [sla_policies_warning_percent_check] CHECK ((([warning_percent] >= 1) AND ([warning_percent] <= 99)));
GO
ALTER TABLE dbo.[ticket_approvals] ADD CONSTRAINT [ticket_approvals_status_check] CHECK (([status] IN ('PENDING', 'APPROVED', 'REJECTED')));
GO
ALTER TABLE dbo.[ticket_attachments] ADD CONSTRAINT [ticket_attachments_size_bytes_check] CHECK ((([size_bytes] > 0) AND ([size_bytes] <= 10485760)));
GO
CREATE UNIQUE INDEX [ticket_attachments_storage_key_unique] ON dbo.[ticket_attachments]([storage_key]);
GO
ALTER TABLE dbo.[ticket_attachments] ADD CONSTRAINT [ticket_attachments_visibility_check] CHECK (([visibility] IN ('INTERNAL', 'PUBLIC')));
GO
ALTER TABLE dbo.[ticket_comments] ADD CONSTRAINT [ticket_comments_comment_type_check] CHECK (([comment_type] IN ('PUBLIC', 'INTERNAL')));
GO
ALTER TABLE dbo.[ticket_prefix_registry] ADD CONSTRAINT [ticket_prefix_registry_check] CHECK ((([owner_kind]='LEGACY' AND [owner_id] IS NULL) OR ([owner_kind]<>'LEGACY' AND [owner_id] IS NOT NULL)));
GO
ALTER TABLE dbo.[ticket_prefix_registry] ADD CONSTRAINT [ticket_prefix_registry_owner_kind_check] CHECK (([owner_kind] IN ('CLIENT', 'SITE', 'LEGACY')));
GO
ALTER TABLE dbo.[ticket_prefix_registry] ADD CONSTRAINT [ticket_prefix_registry_prefix_check] CHECK ((REGEXP_LIKE([prefix], '^[A-Z][A-Z0-9]{1,15}$')));
GO
CREATE UNIQUE INDEX [ticket_sla_ticket_id_unique] ON dbo.[ticket_sla]([ticket_id]);
GO
ALTER TABLE dbo.[tickets] ADD CONSTRAINT [tickets_approval_status_check] CHECK (([approval_status] IN ('NOT_REQUIRED', 'PENDING', 'APPROVED', 'REJECTED')));
GO
CREATE UNIQUE INDEX [tickets_id_client_id_unique] ON dbo.[tickets]([id],[client_id]);
GO
ALTER TABLE dbo.[tickets] ADD CONSTRAINT [tickets_priority_check] CHECK (([priority] IN ('P1_CRITICAL', 'P2_HIGH', 'P3_MEDIUM', 'P4_LOW')));
GO
ALTER TABLE dbo.[tickets] ADD CONSTRAINT [tickets_status_check] CHECK (([status] IN ('NEW', 'OPEN', 'IN_PROGRESS', 'WAITING_FOR_CLIENT', 'WAITING_FOR_VENDOR', 'ON_HOLD', 'RESOLVED', 'CLOSED', 'CANCELLED')));
GO
CREATE UNIQUE INDEX [tickets_ticket_number_unique] ON dbo.[tickets]([ticket_number]);
GO
ALTER TABLE dbo.[users] ADD CONSTRAINT [users_change_access_check] CHECK (([change_access] IN ('NONE', 'CONTRIBUTOR', 'COORDINATOR', 'APPROVER', 'CHANGE_MANAGER')));
GO
CREATE UNIQUE INDEX [users_entra_tenant_id_entra_object_id_unique] ON dbo.[users]([entra_tenant_id],[entra_object_id]);
GO
ALTER TABLE dbo.[users] ADD CONSTRAINT [users_work_scope_check] CHECK (([work_scope] IN ('INTERNAL', 'CLIENT', 'BOTH', 'ALL')));
GO
ALTER TABLE dbo.[access_groups] ADD CONSTRAINT [access_groups_client_id_fkey] FOREIGN KEY ([client_id]) REFERENCES dbo.[clients]([id]);
GO
ALTER TABLE dbo.[assets] ADD CONSTRAINT [assets_client_id_foreign] FOREIGN KEY ([client_id]) REFERENCES dbo.[clients]([id]);
GO
ALTER TABLE dbo.[assets] ADD CONSTRAINT [assets_site_client_fk] FOREIGN KEY ([site_id],[client_id]) REFERENCES dbo.[sites]([id],[client_id]);
GO
ALTER TABLE dbo.[assets] ADD CONSTRAINT [assets_site_id_foreign] FOREIGN KEY ([site_id]) REFERENCES dbo.[sites]([id]);
GO
ALTER TABLE dbo.[audit_logs] ADD CONSTRAINT [audit_logs_actor_user_id_foreign] FOREIGN KEY ([actor_user_id]) REFERENCES dbo.[users]([id]);
GO
ALTER TABLE dbo.[cab_members] ADD CONSTRAINT [cab_members_group_id_fkey] FOREIGN KEY ([group_id]) REFERENCES dbo.[cab_groups]([id]);
GO
ALTER TABLE dbo.[cab_members] ADD CONSTRAINT [cab_members_user_id_fkey] FOREIGN KEY ([user_id]) REFERENCES dbo.[users]([id]);
GO
ALTER TABLE dbo.[categories] ADD CONSTRAINT [categories_parent_id_foreign] FOREIGN KEY ([parent_id]) REFERENCES dbo.[categories]([id]);
GO
ALTER TABLE dbo.[change_approvals] ADD CONSTRAINT [change_approvals_approver_user_id_foreign] FOREIGN KEY ([approver_user_id]) REFERENCES dbo.[users]([id]);
GO
ALTER TABLE dbo.[change_approvals] ADD CONSTRAINT [change_approvals_cab_group_id_fkey] FOREIGN KEY ([cab_group_id]) REFERENCES dbo.[cab_groups]([id]);
GO
ALTER TABLE dbo.[change_approvals] ADD CONSTRAINT [change_approvals_change_id_foreign] FOREIGN KEY ([change_id]) REFERENCES dbo.[changes]([id]);
GO
ALTER TABLE dbo.[change_approvals] ADD CONSTRAINT [change_approvals_decision_by_user_id_foreign] FOREIGN KEY ([decision_by_user_id]) REFERENCES dbo.[users]([id]);
GO
ALTER TABLE dbo.[change_approvals] ADD CONSTRAINT [change_approvals_department_id_foreign] FOREIGN KEY ([department_id]) REFERENCES dbo.[departments]([id]);
GO
ALTER TABLE dbo.[change_attachments] ADD CONSTRAINT [change_attachments_change_id_foreign] FOREIGN KEY ([change_id]) REFERENCES dbo.[changes]([id]);
GO
ALTER TABLE dbo.[change_attachments] ADD CONSTRAINT [change_attachments_uploaded_by_user_id_foreign] FOREIGN KEY ([uploaded_by_user_id]) REFERENCES dbo.[users]([id]);
GO
ALTER TABLE dbo.[change_blackouts] ADD CONSTRAINT [change_blackouts_client_id_fkey] FOREIGN KEY ([client_id]) REFERENCES dbo.[clients]([id]);
GO
ALTER TABLE dbo.[change_blackouts] ADD CONSTRAINT [change_blackouts_created_by_user_id_fkey] FOREIGN KEY ([created_by_user_id]) REFERENCES dbo.[users]([id]);
GO
ALTER TABLE dbo.[change_blackouts] ADD CONSTRAINT [change_blackouts_site_id_client_id_fkey] FOREIGN KEY ([site_id],[client_id]) REFERENCES dbo.[sites]([id],[client_id]);
GO
ALTER TABLE dbo.[change_blackouts] ADD CONSTRAINT [change_blackouts_site_id_fkey] FOREIGN KEY ([site_id]) REFERENCES dbo.[sites]([id]);
GO
ALTER TABLE dbo.[change_communications] ADD CONSTRAINT [change_communications_change_id_foreign] FOREIGN KEY ([change_id]) REFERENCES dbo.[changes]([id]);
GO
ALTER TABLE dbo.[change_communications] ADD CONSTRAINT [change_communications_created_by_user_id_foreign] FOREIGN KEY ([created_by_user_id]) REFERENCES dbo.[users]([id]);
GO
ALTER TABLE dbo.[change_history] ADD CONSTRAINT [change_history_actor_user_id_foreign] FOREIGN KEY ([actor_user_id]) REFERENCES dbo.[users]([id]);
GO
ALTER TABLE dbo.[change_history] ADD CONSTRAINT [change_history_change_id_foreign] FOREIGN KEY ([change_id]) REFERENCES dbo.[changes]([id]);
GO
ALTER TABLE dbo.[change_reviews] ADD CONSTRAINT [change_reviews_actor_user_id_fkey] FOREIGN KEY ([actor_user_id]) REFERENCES dbo.[users]([id]);
GO
ALTER TABLE dbo.[change_reviews] ADD CONSTRAINT [change_reviews_change_id_fkey] FOREIGN KEY ([change_id]) REFERENCES dbo.[changes]([id]);
GO
ALTER TABLE dbo.[change_stakeholders] ADD CONSTRAINT [change_stakeholders_added_by_user_id_foreign] FOREIGN KEY ([added_by_user_id]) REFERENCES dbo.[users]([id]);
GO
ALTER TABLE dbo.[change_stakeholders] ADD CONSTRAINT [change_stakeholders_change_id_foreign] FOREIGN KEY ([change_id]) REFERENCES dbo.[changes]([id]);
GO
ALTER TABLE dbo.[change_stakeholders] ADD CONSTRAINT [change_stakeholders_contact_id_foreign] FOREIGN KEY ([contact_id]) REFERENCES dbo.[contacts]([id]);
GO
ALTER TABLE dbo.[change_stakeholders] ADD CONSTRAINT [change_stakeholders_user_id_foreign] FOREIGN KEY ([user_id]) REFERENCES dbo.[users]([id]);
GO
ALTER TABLE dbo.[change_tasks] ADD CONSTRAINT [change_tasks_assigned_user_id_foreign] FOREIGN KEY ([assigned_user_id]) REFERENCES dbo.[users]([id]);
GO
ALTER TABLE dbo.[change_tasks] ADD CONSTRAINT [change_tasks_change_id_foreign] FOREIGN KEY ([change_id]) REFERENCES dbo.[changes]([id]);
GO
ALTER TABLE dbo.[change_tasks] ADD CONSTRAINT [change_tasks_department_id_foreign] FOREIGN KEY ([department_id]) REFERENCES dbo.[departments]([id]);
GO
ALTER TABLE dbo.[change_teams] ADD CONSTRAINT [change_teams_change_id_foreign] FOREIGN KEY ([change_id]) REFERENCES dbo.[changes]([id]);
GO
ALTER TABLE dbo.[change_teams] ADD CONSTRAINT [change_teams_department_id_foreign] FOREIGN KEY ([department_id]) REFERENCES dbo.[departments]([id]);
GO
ALTER TABLE dbo.[change_templates] ADD CONSTRAINT [change_templates_created_by_user_id_fkey] FOREIGN KEY ([created_by_user_id]) REFERENCES dbo.[users]([id]);
GO
ALTER TABLE dbo.[change_ticket_links] ADD CONSTRAINT [change_ticket_links_change_id_foreign] FOREIGN KEY ([change_id]) REFERENCES dbo.[changes]([id]);
GO
ALTER TABLE dbo.[change_ticket_links] ADD CONSTRAINT [change_ticket_links_linked_by_user_id_foreign] FOREIGN KEY ([linked_by_user_id]) REFERENCES dbo.[users]([id]);
GO
ALTER TABLE dbo.[change_ticket_links] ADD CONSTRAINT [change_ticket_links_ticket_id_foreign] FOREIGN KEY ([ticket_id]) REFERENCES dbo.[tickets]([id]);
GO
ALTER TABLE dbo.[change_updates] ADD CONSTRAINT [change_updates_author_user_id_foreign] FOREIGN KEY ([author_user_id]) REFERENCES dbo.[users]([id]);
GO
ALTER TABLE dbo.[change_updates] ADD CONSTRAINT [change_updates_change_id_foreign] FOREIGN KEY ([change_id]) REFERENCES dbo.[changes]([id]);
GO
ALTER TABLE dbo.[changes] ADD CONSTRAINT [changes_cab_group_id_fkey] FOREIGN KEY ([cab_group_id]) REFERENCES dbo.[cab_groups]([id]);
GO
ALTER TABLE dbo.[changes] ADD CONSTRAINT [changes_client_id_foreign] FOREIGN KEY ([client_id]) REFERENCES dbo.[clients]([id]);
GO
ALTER TABLE dbo.[changes] ADD CONSTRAINT [changes_department_id_foreign] FOREIGN KEY ([department_id]) REFERENCES dbo.[departments]([id]);
GO
ALTER TABLE dbo.[changes] ADD CONSTRAINT [changes_owner_user_id_foreign] FOREIGN KEY ([owner_user_id]) REFERENCES dbo.[users]([id]);
GO
ALTER TABLE dbo.[changes] ADD CONSTRAINT [changes_requested_by_user_id_foreign] FOREIGN KEY ([requested_by_user_id]) REFERENCES dbo.[users]([id]);
GO
ALTER TABLE dbo.[changes] ADD CONSTRAINT [changes_site_id_foreign] FOREIGN KEY ([site_id]) REFERENCES dbo.[sites]([id]);
GO
ALTER TABLE dbo.[client_email_domains] ADD CONSTRAINT [client_email_domains_client_id_fkey] FOREIGN KEY ([client_id]) REFERENCES dbo.[clients]([id]);
GO
ALTER TABLE dbo.[client_email_domains] ADD CONSTRAINT [client_email_domains_site_id_client_id_fkey] FOREIGN KEY ([site_id],[client_id]) REFERENCES dbo.[sites]([id],[client_id]);
GO
ALTER TABLE dbo.[client_mail_routes] ADD CONSTRAINT [client_mail_routes_client_id_fkey] FOREIGN KEY ([client_id]) REFERENCES dbo.[clients]([id]);
GO
ALTER TABLE dbo.[client_ticket_sequences] ADD CONSTRAINT [client_ticket_sequences_client_id_foreign] FOREIGN KEY ([client_id]) REFERENCES dbo.[clients]([id]);
GO
ALTER TABLE dbo.[client_users] ADD CONSTRAINT [client_users_client_id_foreign] FOREIGN KEY ([client_id]) REFERENCES dbo.[clients]([id]);
GO
ALTER TABLE dbo.[client_users] ADD CONSTRAINT [client_users_user_id_foreign] FOREIGN KEY ([user_id]) REFERENCES dbo.[users]([id]);
GO
ALTER TABLE dbo.[contacts] ADD CONSTRAINT [contacts_client_id_foreign] FOREIGN KEY ([client_id]) REFERENCES dbo.[clients]([id]);
GO
ALTER TABLE dbo.[contacts] ADD CONSTRAINT [contacts_site_client_fk] FOREIGN KEY ([site_id],[client_id]) REFERENCES dbo.[sites]([id],[client_id]);
GO
ALTER TABLE dbo.[contacts] ADD CONSTRAINT [contacts_site_id_foreign] FOREIGN KEY ([site_id]) REFERENCES dbo.[sites]([id]);
GO
ALTER TABLE dbo.[contacts] ADD CONSTRAINT [contacts_user_id_foreign] FOREIGN KEY ([user_id]) REFERENCES dbo.[users]([id]);
GO
ALTER TABLE dbo.[customer_group_members] ADD CONSTRAINT [customer_group_members_contact_id_client_id_fkey] FOREIGN KEY ([contact_id],[client_id]) REFERENCES dbo.[contacts]([id],[client_id]);
GO
ALTER TABLE dbo.[customer_group_members] ADD CONSTRAINT [customer_group_members_group_id_client_id_fkey] FOREIGN KEY ([group_id],[client_id]) REFERENCES dbo.[access_groups]([id],[client_id]);
GO
ALTER TABLE dbo.[department_users] ADD CONSTRAINT [department_users_department_id_foreign] FOREIGN KEY ([department_id]) REFERENCES dbo.[departments]([id]);
GO
ALTER TABLE dbo.[department_users] ADD CONSTRAINT [department_users_user_id_foreign] FOREIGN KEY ([user_id]) REFERENCES dbo.[users]([id]);
GO
ALTER TABLE dbo.[help_requests] ADD CONSTRAINT [help_requests_department_id_foreign] FOREIGN KEY ([department_id]) REFERENCES dbo.[departments]([id]);
GO
ALTER TABLE dbo.[help_requests] ADD CONSTRAINT [help_requests_requested_by_user_id_foreign] FOREIGN KEY ([requested_by_user_id]) REFERENCES dbo.[users]([id]);
GO
ALTER TABLE dbo.[help_requests] ADD CONSTRAINT [help_requests_requested_user_id_foreign] FOREIGN KEY ([requested_user_id]) REFERENCES dbo.[users]([id]);
GO
ALTER TABLE dbo.[help_requests] ADD CONSTRAINT [help_requests_responded_by_user_id_foreign] FOREIGN KEY ([responded_by_user_id]) REFERENCES dbo.[users]([id]);
GO
ALTER TABLE dbo.[help_requests] ADD CONSTRAINT [help_requests_ticket_id_foreign] FOREIGN KEY ([ticket_id]) REFERENCES dbo.[tickets]([id]);
GO
ALTER TABLE dbo.[knowledge_articles] ADD CONSTRAINT [knowledge_articles_author_user_id_fkey] FOREIGN KEY ([author_user_id]) REFERENCES dbo.[users]([id]);
GO
ALTER TABLE dbo.[knowledge_articles] ADD CONSTRAINT [knowledge_articles_category_id_fkey] FOREIGN KEY ([category_id]) REFERENCES dbo.[categories]([id]);
GO
ALTER TABLE dbo.[knowledge_articles] ADD CONSTRAINT [knowledge_articles_client_id_fkey] FOREIGN KEY ([client_id]) REFERENCES dbo.[clients]([id]);
GO
ALTER TABLE dbo.[mail_attachments] ADD CONSTRAINT [mail_attachments_message_id_fkey] FOREIGN KEY ([message_id]) REFERENCES dbo.[mail_messages]([id]);
GO
ALTER TABLE dbo.[mail_messages] ADD CONSTRAINT [mail_messages_change_id_fkey] FOREIGN KEY ([change_id]) REFERENCES dbo.[changes]([id]);
GO
ALTER TABLE dbo.[mail_messages] ADD CONSTRAINT [mail_messages_comment_id_fkey] FOREIGN KEY ([comment_id]) REFERENCES dbo.[ticket_comments]([id]);
GO
ALTER TABLE dbo.[mail_messages] ADD CONSTRAINT [mail_messages_communication_id_fkey] FOREIGN KEY ([communication_id]) REFERENCES dbo.[change_communications]([id]);
GO
ALTER TABLE dbo.[mail_messages] ADD CONSTRAINT [mail_messages_created_by_user_id_fkey] FOREIGN KEY ([created_by_user_id]) REFERENCES dbo.[users]([id]);
GO
ALTER TABLE dbo.[mail_messages] ADD CONSTRAINT [mail_messages_ticket_id_fkey] FOREIGN KEY ([ticket_id]) REFERENCES dbo.[tickets]([id]);
GO
ALTER TABLE dbo.[notification_preferences] ADD CONSTRAINT [notification_preferences_user_id_foreign] FOREIGN KEY ([user_id]) REFERENCES dbo.[users]([id]);
GO
ALTER TABLE dbo.[notifications] ADD CONSTRAINT [notifications_change_id_foreign] FOREIGN KEY ([change_id]) REFERENCES dbo.[changes]([id]);
GO
ALTER TABLE dbo.[notifications] ADD CONSTRAINT [notifications_ticket_id_foreign] FOREIGN KEY ([ticket_id]) REFERENCES dbo.[tickets]([id]);
GO
ALTER TABLE dbo.[notifications] ADD CONSTRAINT [notifications_user_id_foreign] FOREIGN KEY ([user_id]) REFERENCES dbo.[users]([id]);
GO
ALTER TABLE dbo.[portal_ticket_submissions] ADD CONSTRAINT [portal_ticket_submissions_domain_rule_id_client_id_fkey] FOREIGN KEY ([domain_rule_id],[client_id]) REFERENCES dbo.[client_email_domains]([id],[client_id]);
GO
ALTER TABLE dbo.[portal_ticket_submissions] ADD CONSTRAINT [portal_ticket_submissions_ticket_id_client_id_fkey] FOREIGN KEY ([ticket_id],[client_id]) REFERENCES dbo.[tickets]([id],[client_id]);
GO
ALTER TABLE dbo.[problem_history] ADD CONSTRAINT [problem_history_actor_user_id_fkey] FOREIGN KEY ([actor_user_id]) REFERENCES dbo.[users]([id]);
GO
ALTER TABLE dbo.[problem_history] ADD CONSTRAINT [problem_history_problem_id_fkey] FOREIGN KEY ([problem_id]) REFERENCES dbo.[problems]([id]);
GO
ALTER TABLE dbo.[problem_tickets] ADD CONSTRAINT [problem_tickets_linked_by_user_id_fkey] FOREIGN KEY ([linked_by_user_id]) REFERENCES dbo.[users]([id]);
GO
ALTER TABLE dbo.[problem_tickets] ADD CONSTRAINT [problem_tickets_problem_id_client_id_fkey] FOREIGN KEY ([problem_id],[client_id]) REFERENCES dbo.[problems]([id],[client_id]);
GO
ALTER TABLE dbo.[problem_tickets] ADD CONSTRAINT [problem_tickets_ticket_id_client_id_fkey] FOREIGN KEY ([ticket_id],[client_id]) REFERENCES dbo.[tickets]([id],[client_id]);
GO
ALTER TABLE dbo.[problems] ADD CONSTRAINT [problems_client_id_fkey] FOREIGN KEY ([client_id]) REFERENCES dbo.[clients]([id]);
GO
ALTER TABLE dbo.[problems] ADD CONSTRAINT [problems_created_by_user_id_fkey] FOREIGN KEY ([created_by_user_id]) REFERENCES dbo.[users]([id]);
GO
ALTER TABLE dbo.[problems] ADD CONSTRAINT [problems_owner_user_id_fkey] FOREIGN KEY ([owner_user_id]) REFERENCES dbo.[users]([id]);
GO
ALTER TABLE dbo.[problems] ADD CONSTRAINT [problems_site_id_fkey] FOREIGN KEY ([site_id]) REFERENCES dbo.[sites]([id]);
GO
ALTER TABLE dbo.[saved_ticket_views] ADD CONSTRAINT [saved_ticket_views_user_id_foreign] FOREIGN KEY ([user_id]) REFERENCES dbo.[users]([id]);
GO
ALTER TABLE dbo.[service_templates] ADD CONSTRAINT [service_templates_category_id_fkey] FOREIGN KEY ([category_id]) REFERENCES dbo.[categories]([id]);
GO
ALTER TABLE dbo.[service_templates] ADD CONSTRAINT [service_templates_client_id_fkey] FOREIGN KEY ([client_id]) REFERENCES dbo.[clients]([id]);
GO
ALTER TABLE dbo.[service_templates] ADD CONSTRAINT [service_templates_created_by_user_id_fkey] FOREIGN KEY ([created_by_user_id]) REFERENCES dbo.[users]([id]);
GO
ALTER TABLE dbo.[site_ticket_sequences] ADD CONSTRAINT [site_ticket_sequences_site_id_fkey] FOREIGN KEY ([site_id]) REFERENCES dbo.[sites]([id]);
GO
ALTER TABLE dbo.[sites] ADD CONSTRAINT [sites_client_id_foreign] FOREIGN KEY ([client_id]) REFERENCES dbo.[clients]([id]);
GO
ALTER TABLE dbo.[sla_calendars] ADD CONSTRAINT [sla_calendars_updated_by_user_id_foreign] FOREIGN KEY ([updated_by_user_id]) REFERENCES dbo.[users]([id]);
GO
ALTER TABLE dbo.[sla_holidays] ADD CONSTRAINT [sla_holidays_calendar_id_foreign] FOREIGN KEY ([calendar_id]) REFERENCES dbo.[sla_calendars]([id]);
GO
ALTER TABLE dbo.[sla_policies] ADD CONSTRAINT [sla_policies_calendar_id_foreign] FOREIGN KEY ([calendar_id]) REFERENCES dbo.[sla_calendars]([id]);
GO
ALTER TABLE dbo.[sla_policies] ADD CONSTRAINT [sla_policies_updated_by_user_id_foreign] FOREIGN KEY ([updated_by_user_id]) REFERENCES dbo.[users]([id]);
GO
ALTER TABLE dbo.[staff_group_members] ADD CONSTRAINT [staff_group_members_group_id_fkey] FOREIGN KEY ([group_id]) REFERENCES dbo.[access_groups]([id]);
GO
ALTER TABLE dbo.[staff_group_members] ADD CONSTRAINT [staff_group_members_user_id_fkey] FOREIGN KEY ([user_id]) REFERENCES dbo.[users]([id]);
GO
ALTER TABLE dbo.[ticket_approvals] ADD CONSTRAINT [ticket_approvals_decided_by_user_id_foreign] FOREIGN KEY ([decided_by_user_id]) REFERENCES dbo.[users]([id]);
GO
ALTER TABLE dbo.[ticket_approvals] ADD CONSTRAINT [ticket_approvals_requested_by_user_id_foreign] FOREIGN KEY ([requested_by_user_id]) REFERENCES dbo.[users]([id]);
GO
ALTER TABLE dbo.[ticket_approvals] ADD CONSTRAINT [ticket_approvals_ticket_id_foreign] FOREIGN KEY ([ticket_id]) REFERENCES dbo.[tickets]([id]);
GO
ALTER TABLE dbo.[ticket_assignments] ADD CONSTRAINT [ticket_assignments_actor_user_id_foreign] FOREIGN KEY ([actor_user_id]) REFERENCES dbo.[users]([id]);
GO
ALTER TABLE dbo.[ticket_assignments] ADD CONSTRAINT [ticket_assignments_assigned_user_id_foreign] FOREIGN KEY ([assigned_user_id]) REFERENCES dbo.[users]([id]);
GO
ALTER TABLE dbo.[ticket_assignments] ADD CONSTRAINT [ticket_assignments_previous_user_id_foreign] FOREIGN KEY ([previous_user_id]) REFERENCES dbo.[users]([id]);
GO
ALTER TABLE dbo.[ticket_assignments] ADD CONSTRAINT [ticket_assignments_ticket_id_foreign] FOREIGN KEY ([ticket_id]) REFERENCES dbo.[tickets]([id]);
GO
ALTER TABLE dbo.[ticket_attachments] ADD CONSTRAINT [ticket_attachments_ticket_id_foreign] FOREIGN KEY ([ticket_id]) REFERENCES dbo.[tickets]([id]);
GO
ALTER TABLE dbo.[ticket_attachments] ADD CONSTRAINT [ticket_attachments_uploaded_by_user_id_foreign] FOREIGN KEY ([uploaded_by_user_id]) REFERENCES dbo.[users]([id]);
GO
ALTER TABLE dbo.[ticket_collaborators] ADD CONSTRAINT [ticket_collaborators_added_by_user_id_foreign] FOREIGN KEY ([added_by_user_id]) REFERENCES dbo.[users]([id]);
GO
ALTER TABLE dbo.[ticket_collaborators] ADD CONSTRAINT [ticket_collaborators_ticket_id_foreign] FOREIGN KEY ([ticket_id]) REFERENCES dbo.[tickets]([id]);
GO
ALTER TABLE dbo.[ticket_collaborators] ADD CONSTRAINT [ticket_collaborators_user_id_foreign] FOREIGN KEY ([user_id]) REFERENCES dbo.[users]([id]);
GO
ALTER TABLE dbo.[ticket_comments] ADD CONSTRAINT [ticket_comments_author_user_id_foreign] FOREIGN KEY ([author_user_id]) REFERENCES dbo.[users]([id]);
GO
ALTER TABLE dbo.[ticket_comments] ADD CONSTRAINT [ticket_comments_ticket_id_foreign] FOREIGN KEY ([ticket_id]) REFERENCES dbo.[tickets]([id]);
GO
ALTER TABLE dbo.[ticket_history] ADD CONSTRAINT [ticket_history_actor_user_id_foreign] FOREIGN KEY ([actor_user_id]) REFERENCES dbo.[users]([id]);
GO
ALTER TABLE dbo.[ticket_history] ADD CONSTRAINT [ticket_history_ticket_id_foreign] FOREIGN KEY ([ticket_id]) REFERENCES dbo.[tickets]([id]);
GO
ALTER TABLE dbo.[ticket_knowledge_links] ADD CONSTRAINT [ticket_knowledge_links_article_id_fkey] FOREIGN KEY ([article_id]) REFERENCES dbo.[knowledge_articles]([id]);
GO
ALTER TABLE dbo.[ticket_knowledge_links] ADD CONSTRAINT [ticket_knowledge_links_linked_by_user_id_fkey] FOREIGN KEY ([linked_by_user_id]) REFERENCES dbo.[users]([id]);
GO
ALTER TABLE dbo.[ticket_knowledge_links] ADD CONSTRAINT [ticket_knowledge_links_ticket_id_fkey] FOREIGN KEY ([ticket_id]) REFERENCES dbo.[tickets]([id]);
GO
ALTER TABLE dbo.[ticket_sla] ADD CONSTRAINT [ticket_sla_calendar_id_foreign] FOREIGN KEY ([calendar_id]) REFERENCES dbo.[sla_calendars]([id]);
GO
ALTER TABLE dbo.[ticket_sla] ADD CONSTRAINT [ticket_sla_policy_id_foreign] FOREIGN KEY ([policy_id]) REFERENCES dbo.[sla_policies]([id]);
GO
ALTER TABLE dbo.[ticket_sla] ADD CONSTRAINT [ticket_sla_ticket_id_foreign] FOREIGN KEY ([ticket_id]) REFERENCES dbo.[tickets]([id]);
GO
ALTER TABLE dbo.[ticket_tags] ADD CONSTRAINT [ticket_tags_added_by_user_id_foreign] FOREIGN KEY ([added_by_user_id]) REFERENCES dbo.[users]([id]);
GO
ALTER TABLE dbo.[ticket_tags] ADD CONSTRAINT [ticket_tags_tag_id_foreign] FOREIGN KEY ([tag_id]) REFERENCES dbo.[tags]([id]);
GO
ALTER TABLE dbo.[ticket_tags] ADD CONSTRAINT [ticket_tags_ticket_id_foreign] FOREIGN KEY ([ticket_id]) REFERENCES dbo.[tickets]([id]);
GO
ALTER TABLE dbo.[tickets] ADD CONSTRAINT [tickets_assigned_user_id_foreign] FOREIGN KEY ([assigned_user_id]) REFERENCES dbo.[users]([id]);
GO
ALTER TABLE dbo.[tickets] ADD CONSTRAINT [tickets_category_id_foreign] FOREIGN KEY ([category_id]) REFERENCES dbo.[categories]([id]);
GO
ALTER TABLE dbo.[tickets] ADD CONSTRAINT [tickets_client_id_foreign] FOREIGN KEY ([client_id]) REFERENCES dbo.[clients]([id]);
GO
ALTER TABLE dbo.[tickets] ADD CONSTRAINT [tickets_created_by_user_id_foreign] FOREIGN KEY ([created_by_user_id]) REFERENCES dbo.[users]([id]);
GO
ALTER TABLE dbo.[tickets] ADD CONSTRAINT [tickets_department_id_foreign] FOREIGN KEY ([department_id]) REFERENCES dbo.[departments]([id]);
GO
ALTER TABLE dbo.[tickets] ADD CONSTRAINT [tickets_requester_client_fk] FOREIGN KEY ([requester_id],[client_id]) REFERENCES dbo.[contacts]([id],[client_id]);
GO
ALTER TABLE dbo.[tickets] ADD CONSTRAINT [tickets_requester_id_foreign] FOREIGN KEY ([requester_id]) REFERENCES dbo.[contacts]([id]);
GO
ALTER TABLE dbo.[tickets] ADD CONSTRAINT [tickets_site_client_fk] FOREIGN KEY ([site_id],[client_id]) REFERENCES dbo.[sites]([id],[client_id]);
GO
ALTER TABLE dbo.[tickets] ADD CONSTRAINT [tickets_site_id_foreign] FOREIGN KEY ([site_id]) REFERENCES dbo.[sites]([id]);
GO
ALTER TABLE dbo.[tickets] ADD CONSTRAINT [tickets_source_fk] FOREIGN KEY ([source]) REFERENCES dbo.[ticket_sources]([code]);
GO
ALTER TABLE dbo.[user_permissions] ADD CONSTRAINT [user_permissions_granted_by_foreign] FOREIGN KEY ([granted_by]) REFERENCES dbo.[users]([id]);
GO
ALTER TABLE dbo.[user_permissions] ADD CONSTRAINT [user_permissions_permission_code_foreign] FOREIGN KEY ([permission_code]) REFERENCES dbo.[permissions]([code]);
GO
ALTER TABLE dbo.[user_permissions] ADD CONSTRAINT [user_permissions_user_id_foreign] FOREIGN KEY ([user_id]) REFERENCES dbo.[users]([id]);
GO
ALTER TABLE dbo.[user_roles] ADD CONSTRAINT [user_roles_granted_by_foreign] FOREIGN KEY ([granted_by]) REFERENCES dbo.[users]([id]);
GO
ALTER TABLE dbo.[user_roles] ADD CONSTRAINT [user_roles_role_code_foreign] FOREIGN KEY ([role_code]) REFERENCES dbo.[roles]([code]);
GO
ALTER TABLE dbo.[user_roles] ADD CONSTRAINT [user_roles_user_id_foreign] FOREIGN KEY ([user_id]) REFERENCES dbo.[users]([id]);
GO
ALTER TABLE dbo.[user_sites] ADD CONSTRAINT [user_sites_site_id_foreign] FOREIGN KEY ([site_id]) REFERENCES dbo.[sites]([id]);
GO
ALTER TABLE dbo.[user_sites] ADD CONSTRAINT [user_sites_user_id_foreign] FOREIGN KEY ([user_id]) REFERENCES dbo.[users]([id]);
GO
ALTER TABLE dbo.[users] ADD CONSTRAINT [users_supervisor_user_id_foreign] FOREIGN KEY ([supervisor_user_id]) REFERENCES dbo.[users]([id]);
GO
