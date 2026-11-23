CREATE TABLE IF NOT EXISTS submission_reviews (
  kind VARCHAR(20) NOT NULL,
  entity_id __USER_ID_DEFINITION__ NOT NULL,
  submitted_by_user_id __USER_ID_DEFINITION__ NOT NULL,
  status VARCHAR(16) NOT NULL DEFAULT 'PENDING',
  revision INT NOT NULL DEFAULT 1,
  submitted_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  reviewed_by_user_id __USER_ID_DEFINITION__ NULL,
  reviewed_at DATETIME(6) NULL,
  comment VARCHAR(2000) NULL,
  PRIMARY KEY (kind,entity_id),
  KEY ix_submission_queue (status,submitted_at),
  KEY ix_submission_author (submitted_by_user_id),
  CONSTRAINT fk_submission_author FOREIGN KEY (submitted_by_user_id) REFERENCES users(id) ON DELETE CASCADE,
  CONSTRAINT fk_submission_reviewer FOREIGN KEY (reviewed_by_user_id) REFERENCES users(id) ON DELETE SET NULL,
  CONSTRAINT ck_submission_status CHECK (status IN ('PENDING','APPROVED','REJECTED')),
  CONSTRAINT ck_submission_revision CHECK (revision>0),
  CONSTRAINT ck_submission_kind CHECK (kind IN ('CHANGE','PROBLEM','KNOWLEDGE','SERVICE'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
