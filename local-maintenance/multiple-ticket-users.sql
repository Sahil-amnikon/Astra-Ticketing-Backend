CREATE TABLE IF NOT EXISTS ticket_additional_users (
  ticket_id __TICKET_ID_DEFINITION__ NOT NULL,
  contact_id __CONTACT_ID_DEFINITION__ NOT NULL,
  PRIMARY KEY (ticket_id,contact_id),
  KEY ix_additional_ticket_contact (contact_id),
  CONSTRAINT fk_additional_ticket FOREIGN KEY (ticket_id) REFERENCES tickets(id) ON DELETE CASCADE,
  CONSTRAINT fk_additional_ticket_contact FOREIGN KEY (contact_id) REFERENCES contacts(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
