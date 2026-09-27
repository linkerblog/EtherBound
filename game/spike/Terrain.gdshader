// Iso-space texturing: a top samples today's 64x32 diamond sheet at its world position projected
// into sheet space, and a vertical face samples the 32x16 side cells column by column, so with the
// camera snapped to the art-pixel grid one texel lands on one art pixel. Mirrors the web client's
// tileMasks.ts / terrainSprites.ts rules (variants, cap rows, height shading).
shader_type spatial;
render_mode cull_disabled, specular_disabled, depth_prepass_alpha;

uniform sampler2DArray tops : filter_nearest, repeat_disable;
uniform sampler2DArray sides : filter_nearest, repeat_disable;
// 0 = iso-space (one texel per screen pixel), 1 = world-aligned 32x32 square texels.
uniform int tex_mode = 0;
uniform bool clip_enabled = false;
uniform float clip_h = 100000.0;
uniform bool cliff_cutaway_enabled = false;
uniform vec3 cliff_cut_center = vec3(0.0);
uniform vec3 cliff_cut_right = vec3(1.0, 0.0, 0.0);
uniform vec3 cliff_cut_up = vec3(0.0, 1.0, 0.0);
uniform bool height_shading = true;

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
	// Codes: 0 terrain/floor top, 1/2 exposed terrain sides; walls, props and furniture stay.
	bool cut_top = is_top && code == 0;
	bool cut_side = !is_top && (code == 1 || code == 2);
	if (cliff_cutaway_enabled && (cut_top || cut_side)) {
		vec3 offset = wpos - cliff_cut_center;
		float horizontal = dot(offset, cliff_cut_right);
		float vertical = dot(offset, cliff_cut_up);
		float screen_radius = length(vec2(horizontal, vertical));
		// One clean circular window: every terrain tile around Niko clears except the floor he stands
		// on, so a tile's top and faces never split into floating crumbs.
		bool upper_top = cut_top && wpos.y > cliff_cut_center.y - 0.8;
		float fade = (1.0 - smoothstep(1.2, 1.5, screen_radius)) * float(cut_side || upper_top);
		ALPHA = 1.0 - fade;
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
	ALBEDO = to_linear(col);
	NORMAL = view_normal;
}

void light() {
	DIFFUSE_LIGHT += clamp(dot(NORMAL, LIGHT), 0.0, 1.0) * ATTENUATION * LIGHT_COLOR / PI;
}
