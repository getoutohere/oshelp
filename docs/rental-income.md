# Apartment revenue comparison

The right tenant panel shows the selected apartment's current occupancy, its actual rent payment, the payment with the selected resident, and the change in OC and RM separately. Each amount names its payment period. Monthly totals before payment rounding appear as a secondary reference. Changing the selected apartment or resident recalculates the comparison immediately.


This is a comparison for the selected apartment at its currently recorded facilities. It assumes the replacement has completed moving in; it does not apply a lease, evict a resident, combine exchange rates, subtract utilities, or count revenue changes in another apartment where that applicant may already live. Eligibility is shown separately below the comparison.

## Verified game rules

The installed assembly's [rental IL](evidence/rental-income-rules.il.txt) establishes:

1. `TenementApartment.GetValue` starts at **5,000**, then adds current upgrade values for building stairs, facade, courtyard, basement sauna, sauna/pier, elevator, and roof, plus all eight apartment facilities. It uses current upgrade objects, resolving a missing cached object from the corresponding group and current tier.
2. Small units retain that value. Medium units multiply by **1.5** using double precision and truncate to integer. Large units multiply by **2**.
3. OC uses divisor **20**, and RM uses divisor **200**, initialized by the apartment class's static constructor.
4. For each currency, `GetRawOcIncome`/`GetRawRmIncome` return zero when that resident's base income is zero. Otherwise they compute `truncate(float32(baseIncome) + float32(value)/float32(divisor))`, multiply that integer by the rental-income multiplier using float32, then truncate again.
5. Current income is zero without a resident, during move-in, or for furniture-only occupancy. Selecting the current renter produces a zero revenue change. Non-rental units have no comparison.
6. `GetOcIncome` divides the raw monthly OC by `DifficultyInfo.GetNumberOfPayments` using float32, then calls `Mathf.RoundToInt`. The installed Unity implementation calls `Math.Round(double)` and converts to integer: midpoint ties round to even. `GetRmIncome` divides using float32, calls `Math.Round(double, 2)`, and converts back to float32.
7. Payment modes are Default/Weekly = **4 payments/month**, Daily = **30**, Biweekly = **2**, Monthly = **1**. Compute each resident's payment with the game's rounding first, then subtract those payments to obtain the replacement change.

The UI deliberately follows the recurring-income functions. `TenementResident.GetMonthlyRent` also adds both components from `GetScaledValue`, even when a base currency is zero, so using that method alone would misstate what an OC-only or RM-only tenant actually pays.

All three configured presets are read directly from the scene. Default rental income is **×1, paid weekly**; Casual is **×2, paid daily**, independently of Casual's ×0.5 renovation costs. Custom income and frequency come from the saved `rentIncome` and `rentPayments` enums.

The reported Apartment 04/Pate discrepancy was a payment-period error: **2,503 OC is the raw monthly total**, while the default weekly payment is `RoundToInt(2503 / 4) = 626 OC`. The previous panel displayed the monthly total as the main revenue figure. The corrected panel makes the actual payment primary.

An example with all facility tiers at zero has **5,100** value in a Small apartment: the 5,000 base plus the starting sauna/pier asset's **100** value. At the default weekly schedule, Pate pays **364 OC**, Aleksi pays **864 OC**, and Agnes pays **126 OC + 70 RM**. Replacing Pate with Agnes changes each payment by **−238 OC + 70 RM**. Their monthly totals are 1,455 OC, 3,455 OC, and 505 OC + 280 RM respectively. The initial pier definition demonstrates why starting states must not be assumed to have zero value.

## Imported and manual state

The importer retains current upgrade asset IDs and move-in status and reads the separate income multiplier and payment mode. These fields are optional additions to existing schema-1 profiles. Existing imported profiles recover them from their original source only when its SHA-256 matches the recorded snapshot; no newer game state is silently substituted. Recovering a missing payment schedule preserves manually recorded income and move-in facts. Manual changes to facility levels clear stale upgrade pointers, and completed construction records the actual new upgrade.

When required playthrough facts are absent, the panel names the facts to record rather than treating an unknown unit as vacant or inventing a facility value or payment schedule. Current occupancy can still be shown independently of the revenue inputs. State exposes **Rental income + payment schedule** and the move-in flag for manual tracking.

The [rental settings evidence](evidence/rental-settings.json) contains the verified constants, preset enum values, and source hashes. `Export-RentalSettings.ps1` regenerates it and rejects changed constant instructions. Startup validates the inputs separately from the planning fingerprint, keeping existing carts and profiles compatible.

Verification covers the reported **2,503 → 626 OC** regression, every payment mode, OC midpoint rounding, RM cents, subtraction after payment rounding, mixed currencies, vacancy, move-in, size factors, separate rental multipliers, cached upgrade values, missing facts, and profile migration. It also checks **100 saved valuations** against their actual source upgrade pointers and exercises the displayed replacement payments and apartment switching in the Windows GUI.
