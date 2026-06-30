"""Generate Drug Lab production recipes for the real compounds. These are hidden until the compound is
discovered (gated at runtime by Patch_RecipeDef_AvailableNow), so a human trial unlocks the medicine as
a normal work bill at the vanilla Drug Lab.
Run: python tools/gen_synthesis_recipes.py
"""
import os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "1.6", "Defs", "RecipeDefs", "Recipes_Synthesis.xml")

# compoundDefNameSuffix, label, [(reagent, count), ...]
COMPOUNDS = [
    ("AdrenalCatalyst", "adrenal catalyst", [("MedicineIndustrial", 1), ("WakeUp", 1), ("Neutroamine", 1)]),
    ("CoagulantSerum", "coagulant serum", [("MedicineHerbal", 1), ("MedicineIndustrial", 1), ("Neutroamine", 1)]),
    ("ImmunoPrimer", "immuno-primer", [("MedicineUltratech", 1), ("Penoxycyline", 1), ("Neutroamine", 1)]),
    ("NeuralDefragmenter", "neural defragmenter", [("MedicineUltratech", 1), ("Luciferium", 1), ("Neutroamine", 1)]),
    ("SomnolentDraught", "somnolent draught", [("MedicineHerbal", 1), ("Beer", 1), ("Neutroamine", 1)]),
    ("BattleStimX", "battle stim x", [("GoJuice", 1), ("WakeUp", 1), ("Yayo", 1)]),
    ("BerserkerDraught", "berserker draught", [("Flake", 1), ("Chemfuel", 1), ("Beer", 1)]),
    ("Stoneskin", "stoneskin compound", [("MedicineIndustrial", 1), ("Luciferium", 1), ("Chemfuel", 1)]),
    ("HepatotoxinB", "hepatotoxin b", [("Beer", 1), ("Yayo", 1), ("Chemfuel", 1)]),
    ("SoporificMist", "soporific mist", [("PsychoidLeaves", 1), ("MedicineHerbal", 1), ("Neutroamine", 1)]),
    ("TissueRegenerant", "tissue regenerant", [("MedicineUltratech", 2), ("Neutroamine", 1)]),
    ("NerveConductionGel", "nerve conduction gel", [("MedicineUltratech", 1), ("Luciferium", 1), ("Penoxycyline", 1)]),
    ("SynapticAccelerant", "synaptic accelerant", [("WakeUp", 2), ("MedicineUltratech", 1)]),
    ("SickDrug", "failed compound", [("MedicineHerbal", 2), ("Chemfuel", 1)]),
    ("LethalDrug", "fatal compound", [("Chemfuel", 1), ("Luciferium", 1), ("Yayo", 1)]),
]

def recipe(suffix, label, reagents):
    product = "ME_Compound_" + suffix
    ing = "".join(
        f"      <li><filter><thingDefs><li>{r}</li></thingDefs></filter><count>{n}</count></li>\n"
        for r, n in reagents)
    fixed = "".join(f"<li>{r}</li>" for r, _ in reagents)
    return (
        f"  <RecipeDef>\n"
        f"    <defName>ME_Synth_{suffix}</defName>\n"
        f"    <label>synthesize {label}</label>\n"
        f"    <description>Produce {label} from its now-known formula.</description>\n"
        f"    <jobString>Synthesizing {label}.</jobString>\n"
        f"    <workSkill>Intellectual</workSkill>\n"
        f"    <workSpeedStat>DrugSynthesisSpeed</workSpeedStat>\n"
        f"    <effectWorking>Cook</effectWorking>\n"
        f"    <soundWorking>Recipe_Drug</soundWorking>\n"
        f"    <workAmount>1800</workAmount>\n"
        f"    <recipeUsers><li>DrugLab</li></recipeUsers>\n"
        f"    <products><{product}>4</{product}></products>\n"
        f"    <ingredients>\n{ing}    </ingredients>\n"
        f"    <fixedIngredientFilter><thingDefs>{fixed}</thingDefs></fixedIngredientFilter>\n"
        f"  </RecipeDef>\n")

parts = ['<?xml version="1.0" encoding="utf-8"?>\n<Defs>\n\n']
parts.append("  <!-- Auto-generated (tools/gen_synthesis_recipes.py). Hidden until the compound is discovered. -->\n\n")
for suffix, label, reagents in COMPOUNDS:
    parts.append(recipe(suffix, label, reagents))
parts.append("\n</Defs>\n")

os.makedirs(os.path.dirname(OUT), exist_ok=True)
with open(OUT, "w", encoding="utf-8") as f:
    f.write("".join(parts))
print(f"wrote {os.path.relpath(OUT, ROOT)} ({len(COMPOUNDS)} synthesis recipes)")
