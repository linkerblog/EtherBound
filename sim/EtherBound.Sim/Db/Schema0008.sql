CREATE TABLE actor (
	id VARCHAR(64) NOT NULL, 
	kind VARCHAR(32) NOT NULL, 
	x FLOAT NOT NULL, 
	y FLOAT NOT NULL, 
	z INTEGER DEFAULT '0' NOT NULL, h INTEGER DEFAULT '0' NOT NULL, activity JSON, mass_kg FLOAT DEFAULT '80' NOT NULL, name VARCHAR(64), mind JSON, 
	PRIMARY KEY (id)
);
CREATE TABLE alembic_version (
	version_num VARCHAR(32) NOT NULL, 
	CONSTRAINT alembic_version_pkc PRIMARY KEY (version_num)
);
CREATE TABLE chunk (
	cx INTEGER NOT NULL, 
	cy INTEGER NOT NULL, 
	ground_h BLOB NOT NULL, 
	surface_mat BLOB NOT NULL, 
	strata JSON NOT NULL, 
	revision INTEGER DEFAULT '0' NOT NULL, 
	gen_version INTEGER NOT NULL, dug BLOB, 
	PRIMARY KEY (cx, cy)
);
CREATE TABLE chunk_level (
	cx INTEGER NOT NULL, 
	cy INTEGER NOT NULL, 
	z INTEGER NOT NULL, 
	floor_h BLOB NOT NULL, 
	floor_mat BLOB NOT NULL, 
	wall_n BLOB NOT NULL, 
	wall_w BLOB NOT NULL, 
	edge_flags BLOB NOT NULL, 
	flags BLOB NOT NULL, 
	PRIMARY KEY (cx, cy, z)
);
CREATE TABLE event (
	seq INTEGER NOT NULL, 
	game_minute INTEGER NOT NULL, 
	type VARCHAR(64) NOT NULL, 
	actor_id VARCHAR(64), 
	data JSON NOT NULL, 
	PRIMARY KEY (seq)
);
CREATE TABLE material (
	id INTEGER NOT NULL, 
	"key" VARCHAR(64) NOT NULL, 
	name VARCHAR(128) NOT NULL, 
	color VARCHAR(16) NOT NULL, 
	walkable BOOLEAN NOT NULL, 
	walk_cost FLOAT NOT NULL, 
	solid BOOLEAN NOT NULL, 
	blocks_sight BOOLEAN NOT NULL, 
	diggable BOOLEAN NOT NULL, 
	dig_cost FLOAT NOT NULL, 
	flammable BOOLEAN NOT NULL, 
	density FLOAT NOT NULL, 
	resistance FLOAT NOT NULL, 
	liquid BOOLEAN NOT NULL, 
	tags JSON NOT NULL, 
	PRIMARY KEY (id), 
	UNIQUE ("key")
);
CREATE TABLE object (
	id INTEGER NOT NULL, 
	kind VARCHAR(64) NOT NULL, 
	loc VARCHAR(8) NOT NULL, 
	x INTEGER, 
	y INTEGER, 
	h INTEGER, 
	cx INTEGER, 
	cy INTEGER, 
	container_id INTEGER, 
	actor_id VARCHAR(64), 
	slot VARCHAR(8), 
	quantity INTEGER DEFAULT '1' NOT NULL, 
	state JSON NOT NULL, 
	integrity FLOAT, 
	owner VARCHAR(64), 
	PRIMARY KEY (id), 
	CONSTRAINT ck_object_quantity CHECK (quantity > 0), 
	CONSTRAINT ck_object_location CHECK ((loc = 'tile' AND x IS NOT NULL AND y IS NOT NULL AND h IS NOT NULL AND cx IS NOT NULL AND cy IS NOT NULL AND container_id IS NULL AND actor_id IS NULL AND slot IS NULL) OR (loc = 'in' AND x IS NULL AND y IS NULL AND h IS NULL AND cx IS NULL AND cy IS NULL AND container_id IS NOT NULL AND actor_id IS NULL AND slot IS NULL) OR (loc = 'held' AND x IS NULL AND y IS NULL AND h IS NULL AND cx IS NULL AND cy IS NULL AND container_id IS NULL AND actor_id IS NOT NULL AND slot IN ('left', 'right', 'both')) OR (loc = 'worn' AND x IS NULL AND y IS NULL AND h IS NULL AND cx IS NULL AND cy IS NULL AND container_id IS NULL AND actor_id IS NOT NULL AND slot = 'back')), 
	FOREIGN KEY(container_id) REFERENCES object (id), 
	FOREIGN KEY(actor_id) REFERENCES actor (id)
);
CREATE TABLE wall_integrity (
	cx INTEGER NOT NULL, 
	cy INTEGER NOT NULL, 
	z INTEGER NOT NULL, 
	cell_index INTEGER NOT NULL, 
	edge VARCHAR(5) NOT NULL, 
	integrity FLOAT NOT NULL, 
	PRIMARY KEY (cx, cy, z, cell_index, edge), 
	CONSTRAINT ck_wall_integrity_cell CHECK (cell_index BETWEEN 0 AND 1023), 
	CONSTRAINT ck_wall_integrity_edge CHECK (edge IN ('north', 'west')), 
	CONSTRAINT ck_wall_integrity_positive CHECK (integrity > 0)
);
CREATE TABLE world_meta (
	id INTEGER NOT NULL, 
	seed BIGINT NOT NULL, 
	game_minute INTEGER DEFAULT '0' NOT NULL, 
	speed INTEGER DEFAULT '1' NOT NULL, 
	paused BOOLEAN DEFAULT 0 NOT NULL, gen_version INTEGER DEFAULT '0' NOT NULL, generator VARCHAR(32) DEFAULT 'test' NOT NULL, gen_options JSON DEFAULT '{}' NOT NULL, 
	PRIMARY KEY (id)
);
CREATE INDEX ix_event_actor_id ON event (actor_id);
CREATE INDEX ix_event_game_minute ON event (game_minute);
CREATE INDEX ix_event_type ON event (type);
CREATE INDEX ix_object_actor_id ON object (actor_id);
CREATE INDEX ix_object_cell ON object (cx, cy);
CREATE INDEX ix_object_container_id ON object (container_id);
