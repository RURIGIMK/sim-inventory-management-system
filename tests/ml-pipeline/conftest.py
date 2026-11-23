"""Synthetic dataset in the same shape as /api/integrations/ml/dataset, mirroring DemoDataSeeder."""
import sys
from pathlib import Path

import numpy as np
import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "src" / "ml-pipeline"))

SCENARIOS = {2: ("roaming_spike", 11, 1), 11: ("roaming_spike", 3, 1), 19: ("roaming_spike", 7, 1),
             5: ("plan_mismatch", 6, 5), 23: ("plan_mismatch", 9, 5), 8: ("dormant_billed", 4, 4),
             16: ("dormant_billed", 10, 4), 13: ("duplicate_charge", 2, 1), 27: ("duplicate_charge", 8, 1)}


def make_dataset(n_sims: int = 30, months: int = 17, seed: int = 4242) -> dict:
    sims, costs = [], []
    periods = [f"{2025 + (5 + i) // 12}-{(5 + i) % 12 + 1:02d}" for i in range(months)]
    for position in range(n_sims):
        sim_id = position + 1
        plan = ["Corporate voice", "Corporate voice + data", "Data bundle", "Field operations"][sim_id % 4]
        fee = 4800.0 if sim_id % 10 == 0 else 900.0 + (sim_id % 6) * 250
        sims.append({"id": sim_id, "operator": ["Safaricom", "Airtel", "Telkom Kenya"][sim_id % 3], "planType": plan,
                     "contractedMonthlyCost": fee, "status": "Available", "costCentre": "OPS-014"})
        rng = np.random.default_rng(seed + sim_id)
        data_base, voice_base = {"Data bundle": (9000, 40), "Corporate voice + data": (4500, 420),
                                 "Field operations": (2500, 650)}.get(plan, (600, 900))
        plan_months = {}
        if position in SCENARIOS:
            name, from_end, length = SCENARIOS[position]
            for k in range(length):
                plan_months[months - from_end + k] = name
        for i, period in enumerate(periods):
            amount = fee * (1 + rng.random() * 0.08 if rng.random() < 0.3 else 1.0)
            data = data_base * (0.7 + rng.random() * 0.6)
            voice = voice_base * (0.7 + rng.random() * 0.6)
            scenario = plan_months.get(i)
            if scenario == "roaming_spike":
                amount, data = fee * (2.4 + rng.random() * 1.4), data * (3 + rng.random() * 3)
            elif scenario == "plan_mismatch":
                amount, data = fee * (1.35 + rng.random() * 0.3), data * (1.8 + rng.random() * 0.5)
            elif scenario == "dormant_billed":
                amount, data, voice = fee, 0, 0
            elif scenario == "duplicate_charge":
                amount = fee * 2 + (amount - fee)
            costs.append({"simId": sim_id, "period": period, "amount": round(amount, 2), "dataUsageMb": int(data),
                          "voiceMinutes": int(voice), "source": "synthetic", "syntheticScenario": scenario})
    return {"sims": sims, "costs": costs, "assignments": [], "employees": [], "reviews": []}


@pytest.fixture
def dataset():
    return make_dataset()
