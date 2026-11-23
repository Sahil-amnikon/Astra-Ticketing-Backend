CREATE TABLE IF NOT EXISTS custom_role_profiles (
  id CHAR(36) NOT NULL PRIMARY KEY,
  name VARCHAR(80) NOT NULL,
  base_role VARCHAR(32) NOT NULL,
  permissions JSON NOT NULL,
  revision INT NOT NULL DEFAULT 1,
  created_by CHAR(36) NOT NULL,
  created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  updated_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  UNIQUE KEY uq_custom_role_name (name)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
CREATE TABLE IF NOT EXISTS user_custom_roles (
  user_id __USER_ID_DEFINITION__ NOT NULL PRIMARY KEY,
  profile_id CHAR(36) NOT NULL,
  KEY ix_custom_role_profile (profile_id),
  CONSTRAINT fk_custom_role_profile FOREIGN KEY (profile_id) REFERENCES custom_role_profiles(id),
  CONSTRAINT fk_custom_role_user FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
