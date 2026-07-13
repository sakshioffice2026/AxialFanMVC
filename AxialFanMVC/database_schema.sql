-- ============================================================
-- AxialFlow Designer — MySQL 8.0 Database Schema
-- Run once to initialise the database
-- ============================================================

CREATE DATABASE IF NOT EXISTS axialfan_db
    CHARACTER SET utf8mb4
    COLLATE utf8mb4_unicode_ci;

USE axialfan_db;

-- ──────────────────────────────────────────────────────────────
-- 1. users
-- ──────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS users (
    id            INT            NOT NULL AUTO_INCREMENT PRIMARY KEY,
    name          VARCHAR(100)   NOT NULL,
    email         VARCHAR(200)   NOT NULL,
    password_hash VARCHAR(500)   NOT NULL,
    role          VARCHAR(20)    NOT NULL DEFAULT 'user',   -- 'user' | 'admin'
    created_at    DATETIME       NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at    DATETIME       NOT NULL DEFAULT CURRENT_TIMESTAMP
                                          ON UPDATE CURRENT_TIMESTAMP,
    UNIQUE KEY uq_users_email (email)
) ENGINE=InnoDB;

-- ──────────────────────────────────────────────────────────────
-- 2. projects
-- ──────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS projects (
    id          INT           NOT NULL AUTO_INCREMENT PRIMARY KEY,
    user_id     INT           NOT NULL,
    name        VARCHAR(200)  NOT NULL,
    description TEXT          NULL,
    status      VARCHAR(20)   NOT NULL DEFAULT 'draft',  -- 'draft' | 'active' | 'archived'
    created_at  DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at  DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP
                                       ON UPDATE CURRENT_TIMESTAMP,
    CONSTRAINT fk_projects_user
        FOREIGN KEY (user_id) REFERENCES users (id)
        ON DELETE CASCADE
) ENGINE=InnoDB;

-- ──────────────────────────────────────────────────────────────
-- 3. blade_profiles  (lookup / seed data)
-- ──────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS blade_profiles (
    id              INT          NOT NULL AUTO_INCREMENT PRIMARY KEY,
    name            VARCHAR(50)  NOT NULL,
    type            VARCHAR(20)  NOT NULL DEFAULT 'NACA',  -- 'NACA' | 'custom'
    coordinate_data LONGTEXT     NULL,      -- JSON x/y coords for custom profiles
    description     TEXT         NULL,
    UNIQUE KEY uq_blade_profiles_name (name)
) ENGINE=InnoDB;

-- Seed standard NACA profiles
INSERT IGNORE INTO blade_profiles (id, name, type, description) VALUES
(1, 'NACA 4412', 'NACA', 'Cambered, general purpose — recommended default'),
(2, 'NACA 2412', 'NACA', 'Low camber, low-pressure applications'),
(3, 'NACA 0012', 'NACA', 'Symmetric, reversible flow'),
(4, 'Flat plate',  'custom', 'Simplified flat blade geometry');

-- ──────────────────────────────────────────────────────────────
-- 4. design_inputs  (wizard step data — one row per design session)
-- ──────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS design_inputs (
    id                    INT            NOT NULL AUTO_INCREMENT PRIMARY KEY,
    project_id            INT            NOT NULL,
    blade_profile_id      INT            NULL,

    -- Step 1 – media
    media_type            VARCHAR(50)    NOT NULL DEFAULT 'Air (standard)',
    temperature_celsius   DOUBLE         NOT NULL DEFAULT 25,
    inlet_pressure_pa     DOUBLE         NOT NULL DEFAULT 101325,
    density_kg_m3         DOUBLE         NOT NULL DEFAULT 1.204,

    -- Step 2 – flow
    flow_rate_m3s         DOUBLE         NOT NULL DEFAULT 0,

    -- Step 3 – pressure
    static_pressure_pa    DOUBLE         NOT NULL DEFAULT 0,
    total_pressure_pa     DOUBLE         NOT NULL DEFAULT 0,

    -- Step 4 – speed
    speed_rpm             INT            NOT NULL DEFAULT 1450,
    motor_poles           VARCHAR(30)    NOT NULL DEFAULT '4-pole / 50 Hz',

    -- Step 5 – geometry
    blade_count           INT            NOT NULL DEFAULT 6,
    tip_diameter_mm       DOUBLE         NOT NULL DEFAULT 1000,
    hub_ratio             DOUBLE         NOT NULL DEFAULT 0.45,
    blade_angle_deg       DOUBLE         NOT NULL DEFAULT 22,
    target_efficiency_pct DOUBLE         NOT NULL DEFAULT 82,
    motor_power_kw        DOUBLE         NOT NULL DEFAULT 2.2,

    created_at            DATETIME       NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT fk_design_inputs_project
        FOREIGN KEY (project_id) REFERENCES projects (id)
        ON DELETE CASCADE,
    CONSTRAINT fk_design_inputs_blade_profile
        FOREIGN KEY (blade_profile_id) REFERENCES blade_profiles (id)
        ON DELETE SET NULL
) ENGINE=InnoDB;

-- ──────────────────────────────────────────────────────────────
-- 5. design_results  (aerodynamic + structural calculation output)
-- ──────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS design_results (
    id                      INT           NOT NULL AUTO_INCREMENT PRIMARY KEY,
    design_input_id         INT           NOT NULL,

    -- Aerodynamic outputs
    specific_speed          DOUBLE        NOT NULL DEFAULT 0,
    tip_speed_ms            DOUBLE        NOT NULL DEFAULT 0,
    hub_diameter_mm         DOUBLE        NOT NULL DEFAULT 0,
    chord_length_mm         DOUBLE        NOT NULL DEFAULT 0,
    blade_span_mm           DOUBLE        NOT NULL DEFAULT 0,
    shaft_power_kw          DOUBLE        NOT NULL DEFAULT 0,
    overall_efficiency_pct  DOUBLE        NOT NULL DEFAULT 0,
    flow_coefficient        DOUBLE        NOT NULL DEFAULT 0,
    pressure_coefficient    DOUBLE        NOT NULL DEFAULT 0,

    -- Structural outputs
    tip_clearance_mm        DOUBLE        NOT NULL DEFAULT 3,
    blade_stress_mpa        DOUBLE        NOT NULL DEFAULT 0,
    safety_factor           DOUBLE        NOT NULL DEFAULT 0,

    status                  VARCHAR(20)   NOT NULL DEFAULT 'ok',  -- 'ok' | 'warning' | 'error'
    warning_messages        JSON          NULL,    -- JSON array of warning strings
    calculated_at           DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT fk_design_results_input
        FOREIGN KEY (design_input_id) REFERENCES design_inputs (id)
        ON DELETE CASCADE,
    UNIQUE KEY uq_design_results_input (design_input_id)   -- 1-to-1
) ENGINE=InnoDB;

-- ──────────────────────────────────────────────────────────────
-- 6. performance_curves
-- ──────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS performance_curves (
    id               INT          NOT NULL AUTO_INCREMENT PRIMARY KEY,
    design_result_id INT          NOT NULL,
    blade_angle_deg  DOUBLE       NOT NULL,
    speed_rpm        INT          NOT NULL,

    -- Comma-separated double arrays (21 points each)
    q_values         MEDIUMTEXT   NOT NULL,    -- m³/s
    dp_values        MEDIUMTEXT   NOT NULL,    -- Pa
    eta_values       MEDIUMTEXT   NOT NULL,    -- %
    kw_values        MEDIUMTEXT   NOT NULL,    -- kW

    generated_at     DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT fk_performance_curves_result
        FOREIGN KEY (design_result_id) REFERENCES design_results (id)
        ON DELETE CASCADE
) ENGINE=InnoDB;

-- ──────────────────────────────────────────────────────────────
-- 7. drawings
-- ──────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS drawings (
    id               INT          NOT NULL AUTO_INCREMENT PRIMARY KEY,
    design_result_id INT          NOT NULL,
    drawing_type     VARCHAR(30)  NOT NULL,   -- 'front_elevation' | 'cross_section' | 'blade_profile'
    svg_data         LONGTEXT     NULL,       -- inline SVG for browser rendering
    dxf_path         VARCHAR(500) NULL,       -- server file path
    pdf_path         VARCHAR(500) NULL,       -- server file path
    generated_at     DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT fk_drawings_result
        FOREIGN KEY (design_result_id) REFERENCES design_results (id)
        ON DELETE CASCADE
) ENGINE=InnoDB;

-- ──────────────────────────────────────────────────────────────
-- 8. export_logs
-- ──────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS export_logs (
    id          INT           NOT NULL AUTO_INCREMENT PRIMARY KEY,
    project_id  INT           NOT NULL,
    user_id     INT           NOT NULL,
    format      VARCHAR(20)   NOT NULL,   -- 'pdf' | 'dxf' | 'xlsx'
    file_path   VARCHAR(500)  NULL,
    exported_at DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT fk_export_logs_project
        FOREIGN KEY (project_id) REFERENCES projects (id)
        ON DELETE CASCADE,
    CONSTRAINT fk_export_logs_user
        FOREIGN KEY (user_id) REFERENCES users (id)
        ON DELETE RESTRICT
) ENGINE=InnoDB;

-- ──────────────────────────────────────────────────────────────
-- Useful indexes for typical query patterns
-- ──────────────────────────────────────────────────────────────
CREATE INDEX idx_projects_user_id       ON projects       (user_id);
CREATE INDEX idx_design_inputs_project  ON design_inputs  (project_id);
CREATE INDEX idx_perf_curves_result     ON performance_curves (design_result_id);
CREATE INDEX idx_drawings_result        ON drawings       (design_result_id);
CREATE INDEX idx_export_logs_project    ON export_logs    (project_id);
CREATE INDEX idx_export_logs_user       ON export_logs    (user_id);
