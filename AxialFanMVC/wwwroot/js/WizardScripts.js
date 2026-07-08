@section Scripts {
<script>
    // ── Wizard navigation ─────────────────────────────────────────────────────
    let currentStep = 1;
    const totalSteps = 5;

    function goToStep(step) {
        if (currentStep !== step) {
            document.getElementById('badge-' + currentStep).classList.remove('d-none');
        }
        currentStep = step;
        updateUI();
    }

    function navigateStep(direction) {
        const newStep = currentStep + direction;
        if (newStep < 1 || newStep > totalSteps) return;
        if (direction === 1) {
            document.getElementById('badge-' + currentStep).classList.remove('d-none');
        }
        currentStep = newStep;
        updateUI();
    }

    function updateUI() {
        document.querySelectorAll('.wizard-step').forEach(el => el.classList.add('d-none'));
        document.getElementById('step-' + currentStep).classList.remove('d-none');

        document.querySelectorAll('#wizardTabs .nav-link').forEach(el => el.classList.remove('active'));
        document.getElementById('tab-' + currentStep).classList.add('active');

        document.getElementById('currentStepInput').value = currentStep;

        const pct = (currentStep / totalSteps) * 100;
        document.getElementById('progressBar').style.width = pct + '%';
        document.getElementById('stepLabel').textContent = 'Step ' + currentStep + ' of ' + totalSteps;
        document.getElementById('stepPct').textContent = Math.round(pct) + '% complete';

        const btnBack = document.getElementById('btnBack');
        currentStep > 1 ? btnBack.classList.remove('d-none') : btnBack.classList.add('d-none');

        const btnNext      = document.getElementById('btnNext');
        const btnCalculate = document.getElementById('btnCalculate');
        if (currentStep === totalSteps) {
            btnNext.classList.add('d-none');
            btnCalculate.classList.remove('d-none');
            updateSummary();
        } else {
            btnNext.classList.remove('d-none');
            btnCalculate.classList.add('d-none');
        }
    }

    function updateSummary() {
        const get = id => document.getElementById(id)?.value ?? '';
        document.getElementById('sum-media').textContent    = get('MediaType');
        document.getElementById('sum-temp').textContent     = get('TemperatureCelsius');
        document.getElementById('sum-density').textContent  = get('DensityKgM3');
        document.getElementById('sum-flow').textContent     = get('FlowRateM3s');
        document.getElementById('sum-pressure').textContent = get('TotalPressurePa');
        document.getElementById('sum-rpm').textContent      = get('SpeedRpm');

        const sel = document.getElementById('bladeProfileSelect');
        const selectedOption = sel?.options[sel.selectedIndex];
        document.getElementById('sum-profile').textContent =
            (sel?.value && selectedOption?.text !== '— Select profile —')
                ? selectedOption.text : '—';
    }

    // ── Hub Diameter auto-calculate ───────────────────────────────────────────
    function calcHubDiameter() {
        const tip      = parseFloat(document.getElementById('TipDiameterMm')?.value) || 0;
        const ratio    = parseFloat(document.getElementById('HubRatio')?.value) || 0;
        const hubInput = document.getElementById('hubDiameterInput');
        const hint     = document.getElementById('hubDiameterHint');

        if (!hubInput) return;

        if (!hubInput.dataset.manualOverride && tip > 0 && ratio > 0) {
            const calc = Math.round(tip * ratio);
            hubInput.value = calc;
            hint.textContent = `= ${tip} × ${ratio} = ${calc} mm (auto)`;
            hint.className = 'form-text text-success';
        }
    }

    // ── Blade Profile dropdown change handler ─────────────────────────────────
    async function onBladeProfileChange(profileId) {
        const card  = document.getElementById('profilePreviewCard');
        const body  = document.getElementById('profilePreviewBody');
        const label = document.getElementById('profilePreviewLabel');

        if (!profileId) {
            card.style.display = 'none';
            document.getElementById('sum-profile').textContent = '—';
            return;
        }

        card.style.display = 'block';

        const sel            = document.getElementById('bladeProfileSelect');
        const selectedOption = sel.options[sel.selectedIndex];
        const profileName    = selectedOption.getAttribute('data-name') || selectedOption.text;
        label.textContent    = profileName;

        body.innerHTML = `
            <div class="text-center py-3 text-muted">
                <div class="spinner-border spinner-border-sm me-2" role="status"></div>
                Loading profile preview…
            </div>`;

        const tipDiam = parseFloat(document.getElementById('tipDiameterInput')?.value) || 0;
        const chord   = tipDiam > 0 ? tipDiam / 2.0 : 148.3;

        try {
            const url = `/BladeProfiles/PreviewPartial?profileId=${encodeURIComponent(profileId)}&chord=${chord}`;
            const res = await fetch(url, { headers: { 'X-Requested-With': 'XMLHttpRequest' } });
            if (!res.ok) throw new Error(`Server returned ${res.status}`);
            body.innerHTML = await res.text();
        } catch (err) {
            body.innerHTML = `
                <div class="alert alert-danger small mb-0">
                    <i class="bi bi-exclamation-triangle me-1"></i>
                    Could not load profile preview: ${err.message}
                </div>`;
        }

        updateSummary();
    }

    // ── Single DOMContentLoaded — all event listeners go here ────────────────
    document.addEventListener('DOMContentLoaded', function () {

        // 1. Initialise wizard UI
        updateUI();

        // 2. Hub diameter — auto-calculate on load
        calcHubDiameter();

        // 3. Hub diameter — manual override detection
        document.getElementById('hubDiameterInput')?.addEventListener('input', function () {
            if (this.value) {
                this.dataset.manualOverride = 'true';
                document.getElementById('hubDiameterHint').textContent = 'Manually entered';
                document.getElementById('hubDiameterHint').className = 'form-text text-primary';
            } else {
                delete this.dataset.manualOverride;
                calcHubDiameter();
            }
        });

        // 4. Hub diameter — recalculate when Tip Diameter changes
        document.getElementById('TipDiameterMm')?.addEventListener('input', calcHubDiameter);

        // 5. Hub diameter — recalculate when Hub Ratio changes
        document.getElementById('HubRatio')?.addEventListener('input', calcHubDiameter);

        // 6. Blade profile preview — re-fetch when tip diameter changes
        let debounce;
        document.getElementById('tipDiameterInput')?.addEventListener('input', () => {
            clearTimeout(debounce);
            debounce = setTimeout(() => {
                const profileId = document.getElementById('bladeProfileSelect')?.value;
                if (profileId) onBladeProfileChange(profileId);
            }, 600);
        });

    });
</script>
}
