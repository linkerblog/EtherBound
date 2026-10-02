CREATE TABLE llm_call (
	id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
	game_minute INTEGER NOT NULL,
	role VARCHAR(32) NOT NULL,
	model VARCHAR(128) NOT NULL,
	tokens_in INTEGER NOT NULL DEFAULT 0,
	tokens_out INTEGER NOT NULL DEFAULT 0,
	cost_usd REAL,
	outcome VARCHAR(16) NOT NULL,
	error TEXT,
	prompt TEXT NOT NULL DEFAULT '',
	response TEXT NOT NULL DEFAULT ''
);

CREATE INDEX ix_llm_call_role ON llm_call (role, id);

UPDATE alembic_version SET version_num = '0011_llm_call';
