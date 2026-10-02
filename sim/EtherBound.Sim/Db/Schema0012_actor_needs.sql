ALTER TABLE actor ADD COLUMN needs JSON;

UPDATE alembic_version SET version_num = '0012_actor_needs';
