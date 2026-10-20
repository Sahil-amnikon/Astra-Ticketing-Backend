-- Preserve sequence values consumed historically, including rolled-back or
-- removed records. Values come from the captured read-only source inventory.
DBCC CHECKIDENT ('dbo.audit_logs', RESEED, 318) WITH NO_INFOMSGS;
DBCC CHECKIDENT ('dbo.ticket_assignments', RESEED, 38) WITH NO_INFOMSGS;
DBCC CHECKIDENT ('dbo.ticket_history', RESEED, 105) WITH NO_INFOMSGS;
DBCC CHECKIDENT ('dbo.change_history', RESEED, 14) WITH NO_INFOMSGS;
GO
