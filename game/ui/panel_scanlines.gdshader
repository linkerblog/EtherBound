shader_type canvas_item;

// Faint horizontal scanlines for HUD panels only (STYLEGUIDE.md [Sec. 15]). The world never gets
// them: a panel overlay draws this, the 3D view stays untouched.
uniform float period = 3.0;
uniform float line_alpha = 0.022;

void fragment() {
	float row = mod(floor(FRAGCOORD.y), period);
	COLOR = vec4(1.0, 1.0, 1.0, row < 1.0 ? line_alpha : 0.0);
}
