"""Generate 40 'sick' + 10 'kill' dummy ExperimentRecipeDefs with unique, collision-free combos.
These flood the combo space so blind/random experimentation usually yields a harmful compound.
Run: python tools/gen_dummy_recipes.py
"""
import os
from itertools import combinations_with_replacement

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "1.6", "Defs", "ExperimentRecipeDefs", "ExperimentRecipes_Dummies.xml")

REAGENTS = [
    "MedicineHerbal", "MedicineIndustrial", "MedicineUltratech",
    "Neutroamine", "Luciferium", "Penoxycyline",
    "GoJuice", "WakeUp", "Yayo", "Flake", "PsychoidLeaves", "SmokeleafLeaves",
    "Ambrosia", "Chemfuel", "Beer",
]

def key(combo):
    return "|".join(sorted(combo))

# Combos already used by hand-authored recipes (exclude so dummies never collide).
USED = [
    ["MedicineIndustrial", "WakeUp", "Neutroamine"],
    ["MedicineHerbal", "MedicineIndustrial", "Neutroamine"],
    ["MedicineUltratech", "Penoxycyline", "Neutroamine"],
    ["MedicineUltratech", "Luciferium", "Neutroamine"],
    ["MedicineHerbal", "Beer", "Neutroamine"],
    ["GoJuice", "WakeUp", "Yayo"],
    ["Flake", "Chemfuel", "Beer"],
    ["MedicineIndustrial", "Luciferium", "Chemfuel"],
    ["Beer", "Yayo", "Chemfuel"],
    ["PsychoidLeaves", "MedicineHerbal", "Neutroamine"],
    ["MedicineUltratech", "MedicineUltratech", "Neutroamine"],
    ["MedicineUltratech", "Luciferium", "Penoxycyline"],
    ["WakeUp", "WakeUp", "MedicineUltratech"],
    ["Luciferium", "MedicineUltratech", "Ambrosia"],
    ["MedicineIndustrial", "Neutroamine", "Chemfuel"],  # refinement
]
used_keys = {key(c) for c in USED}

# All 3-reagent multisets in a deterministic order, skipping used ones.
all_combos = [list(c) for c in combinations_with_replacement(REAGENTS, 3)]
free = [c for c in all_combos if key(c) not in used_keys]

needed = 70
if len(free) < needed:
    raise SystemExit(f"not enough free combos: {len(free)}")

picked = free[:needed]
sick = picked[:40]
kill = picked[40:50]
inert = picked[50:70]

def recipe(defname, product, count, summary, combo):
    counts = {}
    for r in combo:
        counts[r] = counts.get(r, 0) + 1
    lines = "".join(
        f"      <li><thingDef>{r}</thingDef><count>{n}</count></li>\n" for r, n in counts.items())
    return (
        f"  <MedicalExperimentation.ExperimentRecipeDef>\n"
        f"    <defName>{defname}</defName>\n"
        f"    <product>{product}</product>\n"
        f"    <productCount>{count}</productCount>\n"
        # NOT toxic: the flag means "weaponizable via the chemical dispersal unit" (Hepatotoxin B /
        # Soporific Mist). Marking dummies toxic let a discovered 'fatal compound' (killPawn hediff)
        # be loaded into dispersals as an instant-kill AOE, and 'inert' wasted doses.
        f"    <effectSummary>{summary}</effectSummary>\n"
        f"    <reagents>\n{lines}    </reagents>\n"
        f"  </MedicalExperimentation.ExperimentRecipeDef>\n")

parts = ['<?xml version="1.0" encoding="utf-8"?>\n<Defs>\n\n']
parts.append("  <!-- Auto-generated dummy recipes (tools/gen_dummy_recipes.py). 40 sick + 10 lethal + 20 inert. -->\n\n")
for i, c in enumerate(sick, 1):
    parts.append(recipe(f"ME_Exp_Dummy_Sick_{i:03d}", "ME_Compound_SickDrug", 3, "a compound that just sickens", c))
for i, c in enumerate(kill, 1):
    parts.append(recipe(f"ME_Exp_Dummy_Kill_{i:03d}", "ME_Compound_LethalDrug", 3, "a lethal compound", c))
for i, c in enumerate(inert, 1):
    parts.append(recipe(f"ME_Exp_Dummy_Inert_{i:03d}", "ME_Compound_InertDrug", 3, "an inert compound", c))
parts.append("\n</Defs>\n")

os.makedirs(os.path.dirname(OUT), exist_ok=True)
with open(OUT, "w", encoding="utf-8") as f:
    f.write("".join(parts))
print(f"wrote {os.path.relpath(OUT, ROOT)} ({len(sick)} sick + {len(kill)} kill)")
