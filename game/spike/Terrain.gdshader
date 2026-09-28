// Iso-space texturing: a top samples today's 64x32 diamond sheet at its world position projected
// into sheet space, and a vertical face samples the 32x16 side cells column by column, so with the
// camera snapped to the art-pixel grid one texel lands on one art pixel. Mirrors the web client's
// tileMasks.ts / terrainSprites.ts rules (variants, cap rows, height shading).
shader_type spatial;
render_mode cull_disabled, specular_disabled;

uniform sampler2DArray tops : filter_nearest, repeat_disable;
uniform sampler2DArray sides : filter_nearest, repeat_disable;
// 0 = iso-space (one texel per screen pixel), 1 = world-aligned 32x32 square texels.
uniform int tex_mode = 0;
uniform bool clip_enabled = false;
uniform float clip_h = 100000.0;
// Level cut (Dev-033): hide a built fragment whose storey sits above Niko's 3 m band, and cut the
// walls of his own band that stand in front of him to a stub.
uniform bool band_cut = false;
uniform int viewer_band = 0;
uniform float viewer_h = 0.0;
uniform bool wall_stub = false;
uniform bool cliff_cutaway_enabled = false;
uniform vec3 cliff_cut_center = vec3(0.0);
// The cursor's own window, independent of Niko's: both can clear at once, each on the same rules.
uniform bool cursor_cutaway_enabled = false;
uniform vec3 cursor_cut_center = vec3(0.0);
uniform vec3 cliff_cut_right = vec3(1.0, 0.0, 0.0);
uniform vec3 cliff_cut_up = vec3(0.0, 1.0, 0.0);
uniform vec3 cliff_cut_back = vec3(0.5773503);
// Radius of each round line-of-sight window, in screen metres: the size the cliff cut reads well
// with. It can also reach walls and stairs that do not cover him, so Cutaway opens Niko's window
// only when natural terrain itself hides him (Dev-033).
uniform float cliff_cut_radius = 6.0;
uniform bool height_shading = true;

// Mirrors ChunkMesher's face codes (COLOR.a * 8).
const int CODE_WALL_N = 3;
const int CODE_WALL_W = 4;
const int CODE_SLAB_TOP = 6;
const int CODE_SLAB_SIDE = 7;

varying vec3 wpos;
varying vec3 lnormal;
varying vec3 view_normal;

void vertex() {
	wpos = VERTEX;
	lnormal = NORMAL;
	// cull_disabled flips NORMAL on faces seen from behind; light must follow the mesh normal.
	view_normal = normalize((VIEW_MATRIX * vec4(MODEL_NORMAL_MATRIX * NORMAL, 0.0)).xyz);
}

uint mix_hash(uint h) {
	h = (h ^ (h >> 16u)) * 0x45d9f3bu;
	return h ^ (h >> 16u);
}

int top_variant(ivec2 t) {
	return int(mix_hash(uint(t.x) * 0x45d9f3bu ^ uint(t.y) * 0x119de1f3u) & 3u);
}

int side_variant(ivec2 t, int code, int unit_top) {
	uint h = uint(t.x) * 0x45d9f3bu ^ uint(t.y) * 0x119de1f3u ^ uint(unit_top) * 0x27d4eb2fu;
	if (code == 2) h ^= 0x9e3779b9u;
	else if (code == 3) h ^= 0x7f4a7c15u;
	else if (code == 4) h ^= 0x94d049bbu;
	return int(mix_hash(h) & 3u);
}

// spriteTint: the height shade of the material colour, as a per-channel multiplier.
vec3 height_tint(vec3 base, float h, float strength, float min_offset) {
	if (!height_shading) return vec3(1.0);
	float offset = clamp(-h * strength, min_offset, 48.0);
	vec3 c = round(base * 255.0);
	vec3 shaded = clamp(c + offset, 0.0, 255.0);
	vec3 ratio = min(vec3(255.0), round(shaded * 255.0 / max(c, vec3(1.0))));
	return mix(ratio / 255.0, vec3(1.0), vec3(equal(c, vec3(0.0))));
}

vec3 to_linear(vec3 c) {
	return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(0.04045, c));
}

// Distance to the nearest boundary of Niko's window, or a large value when the point is outside it:
// a disc that only opens toward the camera and only above 0.5 m over Niko's feet (the floor and the
// lower stairs stay whole).
float niko_rim(vec3 point, float hh) {
	vec3 offset = point - cliff_cut_center;
	float len = length(vec2(dot(offset, cliff_cut_right), dot(offset, cliff_cut_up)));
	float front = dot(offset, cliff_cut_back);
	float above = hh - viewer_h;
	bool inside = len < cliff_cut_radius && above > 1.0 && front > 0.0;
	float aboveM = above * 0.5;
	float rim = min(min(abs(len - cliff_cut_radius), abs(aboveM - 0.5)), abs(front));
	return inside ? rim : 1e6;
}

// The same for the cursor's window, but fully round: no view-plane cut, so no line crosses its
// center.
float cursor_rim(vec3 point, float hh) {
	vec3 offset = point - cursor_cut_center;
	float len = length(vec2(dot(offset, cliff_cut_right), dot(offset, cliff_cut_up)));
	float above = hh - viewer_h;
	bool inside = len < cliff_cut_radius && above > 1.0;
	float aboveM = above * 0.5;
	float rim = min(abs(len - cliff_cut_radius), abs(aboveM - 0.5));
	return inside ? rim : 1e6;
}

void fragment() {
	float hh = wpos.y * 2.0;
	if (clip_enabled && hh > clip_h + 0.001) discard;
	int code = int(round(COLOR.a * 8.0));
	int layer = int(round(UV2.y));
	float face_top = UV2.x;
	ivec2 owner = ivec2(round(UV));
	vec3 base = COLOR.rgb;
	vec3 col;
	bool is_top = lnormal.y > 0.5;
	// Level cut: a slab's band comes from its floor and a wall's from its top minus one storey, both
	// in face_top, so each build is kept or dropped whole; props and furniture follow their own
	// height. Natural ground (0-2) is left to the circular window.
	if (band_cut) {
		if (code == CODE_SLAB_TOP || code == CODE_SLAB_SIDE) {
			if (int(floor(face_top / 6.0)) > viewer_band) discard;
		} else if (code == CODE_WALL_N || code == CODE_WALL_W) {
			if (int(floor(face_top / 6.0)) - 1 > viewer_band) discard;
		} else if (code >= 5 && int(floor(hh / 6.0)) > viewer_band) {
			discard;
		}
	}
	// Front-wall stub: a wall of Niko's own band on the camera side is cut to half a metre, so it
	// opens the storey instead of hiding him; the walls behind him stay whole (Dev-008, Dev-033).
	if (wall_stub && (code == CODE_WALL_N || code == CODE_WALL_W)) {
		bool north = code == CODE_WALL_N;
		int nx = int(floor(cliff_cut_center.x));
		int ny = int(floor(cliff_cut_center.z));
		bool front = north ? owner.y > ny : owner.x > nx;
		bool same_band = int(floor(face_top / 6.0)) - 1 == viewer_band;
		if (front && same_band && hh > viewer_h + 1.0) discard;
	}
	// The cliff windows cut natural ground only: 0 top, 1/2 exposed sides. Built slabs (6/7),
	// walls (3/4), props (5) and furniture stay, so floors and stairs keep their shape.
	bool cut_top = is_top && code == 0;
	bool cut_side = !is_top && (code == 1 || code == 2);
	bool cut_rim = false;
	if (!IN_SHADOW_PASS && (cliff_cutaway_enabled || cursor_cutaway_enabled) && (cut_top || cut_side)) {
		// Niko's and the cursor's round holes fuse into one area: the rim only traces the outer
		// boundary of the union, so where they overlap there is no seam between them. Each keeps its
		// own clips (Niko's never opens behind him; the cursor's is fully round), and the rim draws
		// with the outline cream, as the silhouette geometry does, so the opening is traced.
		float niko = cliff_cutaway_enabled ? niko_rim(wpos, hh) : 1e6;
		float cursor = cursor_cutaway_enabled ? cursor_rim(wpos, hh) : 1e6;
		bool inNiko = niko < 1e5;
		bool inCursor = cursor < 1e5;
		if (inNiko || inCursor) {
			float rim = inNiko && inCursor ? max(niko, cursor) : min(niko, cursor);
			cut_rim = rim <= 0.08;
			// Discard, never alpha-zero: the material stays opaque and writes no depth where it
			// clears. The shadow pass skips this whole block (IN_SHADOW_PASS), so the removed mass
			// keeps casting exactly as if it were intact (Dev-033).
			if (!cut_rim) discard;
		}
	}
	if (is_top) {
		ivec2 tile = ivec2(floor(wpos.xz));
		vec2 f = clamp(wpos.xz - vec2(tile), 0.0, 0.99999);
		if (tex_mode == 1) f = (floor(f * 32.0) + 0.5) / 32.0;
		vec3 tint = code >= 3 && code <= 4 ? vec3(1.0) : height_tint(base, face_top, 5.0, -40.0);
		if (layer >= 0) {
			ivec2 px = clamp(ivec2(floor(vec2(32.0 + (f.x - f.y) * 32.0, (f.x + f.y) * 16.0))), ivec2(0), ivec2(63, 31));
			vec4 texel = texelFetch(tops, ivec3(top_variant(tile) * 64 + px.x, px.y, layer), 0);
			col = texel.a > 0.5 ? texel.rgb * tint : base * tint;
		} else {
			col = base * tint;
		}
	} else {
		float along;
		if (lnormal.z > 0.5) along = fract(wpos.x);
		else if (lnormal.x > 0.5) along = 1.0 - fract(wpos.z);
		else if (lnormal.z < -0.5) along = 1.0 - fract(wpos.x);
		else along = fract(wpos.z);
		int unit_top = int(floor(hh)) + 1;
		bool walls = code == 3 || code == 4;
		vec3 tint = walls ? vec3(1.0) : height_tint(base, face_top > -9000.0 ? face_top : float(unit_top), 3.0, -20.0);
		if (layer >= 0) {
			int column = clamp(int(floor(along * 32.0)), 0, 31);
			int row = clamp(int(floor((float(unit_top) - hh) * 16.0)), 0, 15);
			int part = int(face_top) == unit_top ? 0 : 16;
			int variant = side_variant(owner, code, unit_top);
			col = texelFetch(sides, ivec3(variant * 32 + column, part + row, layer), 0).rgb * tint;
		} else {
			col = base * tint;
		}
	}
	// The rim reads as the silhouette, so it takes the same cream as ChunkMesher.OutlineColor.
	if (cut_rim) col = vec3(0.93, 0.90, 0.82);
	ALBEDO = to_linear(col);
	NORMAL = view_normal;
}

void light() {
	DIFFUSE_LIGHT += clamp(dot(NORMAL, LIGHT), 0.0, 1.0) * ATTENUATION * LIGHT_COLOR / PI;
}
