from math import sqrt

GRAVITY_M_S2 = 9.81
ROLLING_FRICTION = 0.05
SHOVE_SPEED_M_S = 2.0
MAX_THROW_SPEED_M_S = 8.0
MAX_THROW_ENERGY_J = 100.0
HAND_EFFECTIVE_MASS_KG = 2.0
HAND_SPEED_M_S = 5.0
TOOL_SPEED_M_S = 15.0
MAX_TILE_STEPS = 256


def kinetic_energy(mass_kg: float, speed_m_s: float) -> float:
    return 0.5 * mass_kg * speed_m_s**2


def potential_energy(mass_kg: float, height_m: float) -> float:
    return mass_kg * GRAVITY_M_S2 * height_m


def integrity_capacity(resistance_j_per_half_metre: float, height_cells: int) -> float:
    return resistance_j_per_half_metre * max(1, height_cells)


def absorb_energy(energy_j: float, integrity_j: float) -> tuple[float, float]:
    absorbed = min(energy_j, integrity_j)
    return absorbed, max(0.0, energy_j - absorbed)


def shove_impulse(actor_mass_kg: float, target_mass_kg: float) -> float:
    reduced_mass = actor_mass_kg * target_mass_kg / (actor_mass_kg + target_mass_kg)
    return reduced_mass * SHOVE_SPEED_M_S


def throw_speed(mass_kg: float) -> float:
    return min(MAX_THROW_SPEED_M_S, sqrt(2 * MAX_THROW_ENERGY_J / mass_kg))


def strike_energy(tool_mass_kg: float | None, strike_speed_m_s: float = TOOL_SPEED_M_S) -> float:
    if tool_mass_kg is None:
        return kinetic_energy(HAND_EFFECTIVE_MASS_KG, HAND_SPEED_M_S)
    return kinetic_energy(tool_mass_kg, strike_speed_m_s)


def travel_energy_cost(mass_kg: float) -> float:
    return ROLLING_FRICTION * mass_kg * GRAVITY_M_S2
