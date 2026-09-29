-- Dev-036 [Sec. 3]: the six wall slots per tile. `slot_mask` is the bitmask of the slots that
-- carry a wall (`north`, `west`, `H`, `V`, `D1`, `D2` as N=1, W=2, H=4, V=8, D1=16, D2=32) and
-- `slot_mat` their materials. Both are nullable and NULL means all zeros, so a `0008` chunk blob
-- keeps loading unchanged. Integrity moves to one namespace for edge and interior slots; the old
-- `wall_integrity.edge` is already the slot letter, so the rows are copied as they are.
ALTER TABLE chunk_level ADD COLUMN slot_mask BLOB;
ALTER TABLE chunk_level ADD COLUMN slot_mat BLOB;
CREATE TABLE wall_slot (
	cx INTEGER NOT NULL,
	cy INTEGER NOT NULL,
	z INTEGER NOT NULL,
	cell_index INTEGER NOT NULL,
	slot VARCHAR(4) NOT NULL,
	joules FLOAT NOT NULL,
	PRIMARY KEY (cx, cy, z, cell_index, slot),
	CONSTRAINT ck_wall_slot_cell CHECK (cell_index BETWEEN 0 AND 1023),
	CONSTRAINT ck_wall_slot_slot CHECK (slot IN ('north', 'west', 'H', 'V', 'D1', 'D2')),
	CONSTRAINT ck_wall_slot_positive CHECK (joules > 0)
);
INSERT INTO wall_slot (cx, cy, z, cell_index, slot, joules)
	SELECT cx, cy, z, cell_index, edge, integrity FROM wall_integrity;
DROP TABLE wall_integrity;
UPDATE alembic_version SET version_num = '0009_walls';
