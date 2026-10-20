SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON; SET ANSI_PADDING ON; SET ANSI_WARNINGS ON; SET ARITHABORT ON; SET CONCAT_NULL_YIELDS_NULL ON; SET NUMERIC_ROUNDABORT OFF;
GO
ALTER TABLE dbo.[access_groups] ADD [astra_idx_access_groups_name_uq_1] AS (COALESCE(client_id, CONVERT(uniqueidentifier,'00000000-0000-0000-0000-000000000000'))) PERSISTED;
GO
ALTER TABLE dbo.[access_groups] ADD [astra_idx_access_groups_name_uq_2] AS (lower(TRIM(name))) PERSISTED;
GO
CREATE UNIQUE INDEX [access_groups_name_uq] ON dbo.[access_groups]([kind],[astra_idx_access_groups_name_uq_1],[astra_idx_access_groups_name_uq_2]);
GO
CREATE INDEX [assets_client_idx] ON dbo.[assets]([client_id]);
GO
ALTER TABLE dbo.[assets] ADD [astra_idx_assets_serial_ci_uq_0] AS (lower(serial_number)) PERSISTED;
GO
CREATE UNIQUE INDEX [assets_serial_ci_uq] ON dbo.[assets]([astra_idx_assets_serial_ci_uq_0]) WHERE ([serial_number] IS NOT NULL);
GO
CREATE INDEX [audit_actor_idx] ON dbo.[audit_logs]([actor_user_id],[created_at] DESC);
GO
CREATE INDEX [audit_entity_idx] ON dbo.[audit_logs]([entity_type],[entity_id],[created_at] DESC);
GO
ALTER TABLE dbo.[categories] ADD [astra_idx_categories_child_name_ci_uq_1] AS (lower(name)) PERSISTED;
GO
CREATE UNIQUE INDEX [categories_child_name_ci_uq] ON dbo.[categories]([parent_id],[astra_idx_categories_child_name_ci_uq_1]) WHERE ([parent_id] IS NOT NULL);
GO
ALTER TABLE dbo.[categories] ADD [astra_idx_categories_root_name_ci_uq_0] AS (lower(name)) PERSISTED;
GO
CREATE UNIQUE INDEX [categories_root_name_ci_uq] ON dbo.[categories]([astra_idx_categories_root_name_ci_uq_0]) WHERE ([parent_id] IS NULL);
GO
CREATE UNIQUE INDEX [change_cab_vote_unique] ON dbo.[change_approvals]([change_id],[revision],[approver_user_id]) WHERE ([cab_group_id] IS NOT NULL);
GO
CREATE UNIQUE INDEX [change_stakeholders_contact_unique] ON dbo.[change_stakeholders]([change_id],[contact_id]) WHERE ([contact_id] IS NOT NULL);
GO
ALTER TABLE dbo.[change_stakeholders] ADD [astra_idx_change_stakeholders_email_unique_1] AS (lower(external_email)) PERSISTED;
GO
CREATE UNIQUE INDEX [change_stakeholders_email_unique] ON dbo.[change_stakeholders]([change_id],[astra_idx_change_stakeholders_email_unique_1]) WHERE ([external_email] IS NOT NULL);
GO
CREATE UNIQUE INDEX [change_stakeholders_user_unique] ON dbo.[change_stakeholders]([change_id],[user_id]) WHERE ([user_id] IS NOT NULL);
GO
CREATE INDEX [change_window_idx] ON dbo.[changes]([client_id],[planned_start],[planned_end]);
GO
CREATE INDEX [changes_scope_idx] ON dbo.[changes]([client_id],[status],[planned_start]);
GO
CREATE INDEX [client_email_domains_client_idx] ON dbo.[client_email_domains]([client_id]);
GO
ALTER TABLE dbo.[clients] ADD [astra_idx_clients_name_ci_uq_0] AS (lower(name)) PERSISTED;
GO
CREATE UNIQUE INDEX [clients_name_ci_uq] ON dbo.[clients]([astra_idx_clients_name_ci_uq_0]);
GO
CREATE INDEX [contacts_client_idx] ON dbo.[contacts]([client_id]);
GO
ALTER TABLE dbo.[contacts] ADD [astra_idx_contacts_email_ci_idx_0] AS (lower(email)) PERSISTED;
GO
CREATE INDEX [contacts_email_ci_idx] ON dbo.[contacts]([astra_idx_contacts_email_ci_idx_0]);
GO
CREATE UNIQUE INDEX [contacts_group_client_uq] ON dbo.[contacts]([id],[client_id]);
GO
ALTER TABLE dbo.[contacts] ADD [astra_idx_contacts_portal_email_idx_1] AS (lower(email)) PERSISTED;
GO
CREATE INDEX [contacts_portal_email_idx] ON dbo.[contacts]([client_id],[astra_idx_contacts_portal_email_idx_1]) WHERE [active]=1;
GO
CREATE INDEX [contacts_site_idx] ON dbo.[contacts]([site_id]) WHERE ([site_id] IS NOT NULL);
GO
CREATE INDEX [customer_group_members_contact_idx] ON dbo.[customer_group_members]([contact_id]);
GO
CREATE INDEX [mail_change_idx] ON dbo.[mail_messages]([change_id],[created_at]);
GO
CREATE UNIQUE INDEX [mail_comment_once] ON dbo.[mail_messages]([comment_id]) WHERE (([direction] = 'OUTBOUND') AND ([comment_id] IS NOT NULL));
GO
CREATE UNIQUE INDEX [mail_communication_once] ON dbo.[mail_messages]([communication_id]) WHERE (([direction] = 'OUTBOUND') AND ([communication_id] IS NOT NULL));
GO
CREATE INDEX [mail_outbox_idx] ON dbo.[mail_messages]([state],[next_attempt_at]);
GO
CREATE INDEX [mail_ticket_idx] ON dbo.[mail_messages]([ticket_id],[created_at]);
GO
CREATE INDEX [notifications_user_unread_idx] ON dbo.[notifications]([user_id],[read_at],[created_at] DESC);
GO
CREATE INDEX [problem_history_timeline] ON dbo.[problem_history]([problem_id],[created_at],[id]);
GO
CREATE INDEX [problem_tickets_ticket] ON dbo.[problem_tickets]([ticket_id]);
GO
CREATE INDEX [problems_client_status_updated] ON dbo.[problems]([client_id],[status],[updated_at] DESC,[id]);
GO
CREATE INDEX [problems_owner_updated] ON dbo.[problems]([owner_user_id],[updated_at] DESC,[id]);
GO
ALTER TABLE dbo.[saved_ticket_views] ADD [astra_idx_saved_ticket_views_user_name_uq_1] AS (lower(name)) PERSISTED;
GO
CREATE UNIQUE INDEX [saved_ticket_views_user_name_uq] ON dbo.[saved_ticket_views]([user_id],[astra_idx_saved_ticket_views_user_name_uq_1]);
GO
CREATE UNIQUE INDEX [sites_id_client_portal_uq] ON dbo.[sites]([id],[client_id]);
GO
CREATE UNIQUE INDEX [sites_ticket_prefix_uq] ON dbo.[sites]([ticket_prefix]);
GO
CREATE INDEX [staff_group_members_user_idx] ON dbo.[staff_group_members]([user_id]);
GO
ALTER TABLE dbo.[tags] ADD [astra_idx_tags_name_ci_uq_0] AS (lower(name)) PERSISTED;
GO
CREATE UNIQUE INDEX [tags_name_ci_uq] ON dbo.[tags]([astra_idx_tags_name_ci_uq_0]);
GO
CREATE UNIQUE INDEX [ticket_approvals_pending_uq] ON dbo.[ticket_approvals]([ticket_id]) WHERE ([status] = 'PENDING');
GO
CREATE INDEX [ticket_assignments_ticket_idx] ON dbo.[ticket_assignments]([ticket_id],[created_at]);
GO
CREATE INDEX [ticket_attachments_ticket_idx] ON dbo.[ticket_attachments]([ticket_id],[created_at]);
GO
CREATE INDEX [ticket_comments_ticket_idx] ON dbo.[ticket_comments]([ticket_id],[created_at]);
GO
CREATE INDEX [ticket_history_ticket_idx] ON dbo.[ticket_history]([ticket_id],[created_at],[id]);
GO
CREATE INDEX [ticket_sla_due_idx] ON dbo.[ticket_sla]([response_due_at],[resolution_due_at]);
GO
CREATE INDEX [tickets_assignee_status_idx] ON dbo.[tickets]([assigned_user_id],[status],[updated_at] DESC);
GO
CREATE INDEX [tickets_client_created_idx] ON dbo.[tickets]([client_id],[created_at] DESC);
GO
CREATE INDEX [tickets_status_priority_idx] ON dbo.[tickets]([status],[priority],[updated_at] DESC);
GO
ALTER TABLE dbo.[users] ADD [astra_idx_users_email_ci_uq_0] AS (lower(email)) PERSISTED;
GO
CREATE UNIQUE INDEX [users_email_ci_uq] ON dbo.[users]([astra_idx_users_email_ci_uq_0]);
GO
