CREATE TABLE input_journal (
	seq INTEGER NOT NULL PRIMARY KEY,
	kind VARCHAR(32) NOT NULL,
	payload JSON NOT NULL,
	repeat_count INTEGER NOT NULL DEFAULT 1,
	CONSTRAINT ck_input_repeat_count CHECK (repeat_count > 0)
);

CREATE TABLE activity_work (
	actor_id VARCHAR(64) NOT NULL,
	action_key TEXT NOT NULL,
	progress_minutes INTEGER NOT NULL,
	PRIMARY KEY (actor_id, action_key),
	CONSTRAINT ck_activity_work_progress CHECK (progress_minutes > 0)
);

UPDATE alembic_version SET version_num = '0010_replay_work';
