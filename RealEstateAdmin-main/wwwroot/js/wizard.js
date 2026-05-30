/**
 * DAR.COM — Wizard JS
 * Handles: step navigation, per-step validation, gouvernorat cascade,
 * stepper, equipment toggles, photo upload + ML verify, description
 * generation, price prediction, recap, and publish.
 */
(function () {
    'use strict';

    const STORAGE_KEY = 'darcom_wizard_state';
    const AFT_META    = 'meta[name="request-verification-token"]';

    // ─────────────────────────────── STATE ───────────────────────────────
    /** Single source of truth for the whole form. Persists in sessionStorage. */
    let state = {
        currentStep: 1,
        // Step 1
        gouvernorat: '',
        delegation: '',
        surfaceM2: '',
        nbChambres: 0,
        hasAscenseur: false,
        hasBalcon: false,
        hasChauffageCentral: false,
        hasClimatisation: false,
        hasGarage: false,
        hasJardin: false,
        hasParking: false,
        hasPiscine: false,
        hasTerrasse: false,
        // Step 2
        photos: [],   // [{ url, name }]
        description: '',
        // Step 3
        prixTnd: ''
    };

    function saveState() {
        try { sessionStorage.setItem(STORAGE_KEY, JSON.stringify(state)); } catch (_) {}
    }

    function loadState() {
        try {
            const raw = sessionStorage.getItem(STORAGE_KEY);
            if (raw) Object.assign(state, JSON.parse(raw));
        } catch (_) {}
    }

    // ─────────────────────────────── ANTI-FORGERY ────────────────────────
    function aft() {
        const meta = document.querySelector(AFT_META);
        return meta ? meta.content : '';
    }

    // ─────────────────────────────── STEP RENDERING ──────────────────────
    function showStep(n) {
        state.currentStep = n;
        saveState();

        document.querySelectorAll('.wz-step').forEach(el => el.classList.add('d-none'));
        const target = document.querySelector(`.wz-step[data-step="${n}"]`);
        if (target) target.classList.remove('d-none');

        updateProgressBar(n);
        updateNavButtons(n);
        updateIllusDots(n);

        if (n === 3) buildRecap();
    }

    function updateProgressBar(n) {
        document.querySelectorAll('.wz-progress-seg').forEach(seg => {
            const seg_n = parseInt(seg.dataset.seg, 10);
            seg.classList.toggle('active', seg_n <= n);
        });
    }

    function updateNavButtons(n) {
        const btnBack = document.getElementById('btnBack');
        const btnNext = document.getElementById('btnNext');

        btnBack.classList.toggle('invisible', n === 1);

        if (n === 3) {
            btnNext.innerHTML = 'Publier <i class="bi bi-check-lg"></i>';
            btnNext.classList.add('publish');
        } else {
            btnNext.innerHTML = 'Suivant <i class="bi bi-arrow-right"></i>';
            btnNext.classList.remove('publish');
        }
    }

    function updateIllusDots(n) {
        document.querySelectorAll('.wz-dot').forEach(dot => {
            dot.classList.toggle('active', parseInt(dot.dataset.dot, 10) === n);
        });
    }

    // ─────────────────────────────── VALIDATION ──────────────────────────
    function clearErrors() {
        document.querySelectorAll('.wz-error').forEach(el => el.textContent = '');
        document.querySelectorAll('.is-invalid').forEach(el => el.classList.remove('is-invalid'));
    }

    function setError(fieldId, msg) {
        const errEl = document.getElementById('err-' + fieldId);
        const inputEl = document.getElementById(fieldId);
        if (errEl) errEl.textContent = msg;
        if (inputEl) inputEl.classList.add('is-invalid');
    }

    function validateStep1() {
        clearErrors();
        let ok = true;

        if (!state.gouvernorat) {
            setError('gouvernorat', 'Veuillez sélectionner un gouvernorat.');
            ok = false;
        }
        if (!state.delegation) {
            setError('delegation', 'Veuillez sélectionner une délégation.');
            ok = false;
        }
        const surface = parseInt(state.surfaceM2, 10);
        if (!surface || surface < 1) {
            setError('surface', 'La surface doit être d\'au moins 1 m².');
            ok = false;
        }
        return ok;
    }

    function validateStep3() {
        clearErrors();
        let ok = true;
        const prix = parseInt(state.prixTnd, 10);
        if (!prix || prix < 1) {
            setError('prix', 'Le prix doit être d\'au moins 1 TND.');
            ok = false;
        }
        return ok;
    }

    // ─────────────────────────────── GOUVERNORAT CASCADE ─────────────────
    function populateDelegations(gouvernorat) {
        const select = document.getElementById('delegation');
        select.innerHTML = '<option value="">-- Sélectionnez --</option>';
        const delegations = (typeof GOUVERNORATS_DATA !== 'undefined' && GOUVERNORATS_DATA[gouvernorat]) || [];

        delegations.forEach(d => {
            const opt = document.createElement('option');
            opt.value = d;
            opt.textContent = d;
            if (d === state.delegation) opt.selected = true;
            select.appendChild(opt);
        });

        select.disabled = delegations.length === 0;
    }

    // ─────────────────────────────── SYNC FORM ← STATE ──────────────────
    function syncFormFromState() {
        const gov = document.getElementById('gouvernorat');
        if (gov) gov.value = state.gouvernorat;

        populateDelegations(state.gouvernorat);

        const del = document.getElementById('delegation');
        if (del) del.value = state.delegation;

        const surf = document.getElementById('surface');
        if (surf) surf.value = state.surfaceM2;

        document.getElementById('val-chambres').textContent = state.nbChambres;

        document.querySelectorAll('.wz-equip-card').forEach(card => {
            const key = card.dataset.equip;
            card.classList.toggle('active', !!state[key]);
        });

        const desc = document.getElementById('description');
        if (desc) desc.value = state.description;

        const prix = document.getElementById('prix');
        if (prix) prix.value = state.prixTnd;

        rebuildPhotoGrid();
    }

    // ─────────────────────────────── EQUIPMENT TOGGLES ───────────────────
    function bindEquipmentCards() {
        document.querySelectorAll('.wz-equip-card').forEach(card => {
            card.addEventListener('click', () => {
                const key = card.dataset.equip;
                state[key] = !state[key];
                card.classList.toggle('active', state[key]);
                saveState();
            });
        });
    }

    // ─────────────────────────────── PHOTO UPLOAD ────────────────────────
    function bindPhotoUpload() {
        const dropZone  = document.getElementById('dropZone');
        const fileInput = document.getElementById('photoInput');

        dropZone.addEventListener('click', () => fileInput.click());
        dropZone.addEventListener('dragover', e => {
            e.preventDefault();
            dropZone.classList.add('drag-over');
        });
        dropZone.addEventListener('dragleave', () => dropZone.classList.remove('drag-over'));
        dropZone.addEventListener('drop', e => {
            e.preventDefault();
            dropZone.classList.remove('drag-over');
            handleFiles(e.dataTransfer.files);
        });

        fileInput.addEventListener('change', () => {
            handleFiles(fileInput.files);
            fileInput.value = '';
        });
    }

    function handleFiles(fileList) {
        Array.from(fileList).forEach(file => uploadAndVerify(file));
    }

    // Compteur de photos en cours d'upload
    let _uploadingCount = 0;

    function uploadAndVerify(file) {
        const grid  = document.getElementById('photoGrid');
        const errEl = document.getElementById('photoError');
        errEl.style.display = 'none';

        // Placeholder spinner pendant l'upload
        const thumb = document.createElement('div');
        thumb.className = 'wz-photo-thumb';
        thumb.innerHTML = '<div class="wz-photo-uploading"><div class="spinner-border spinner-border-sm"></div></div>';
        grid.appendChild(thumb);
        _uploadingCount++;

        const formData = new FormData();
        formData.append('photo', file);

        fetch('/Annonce/UploadAndVerify', {
            method: 'POST',
            headers: { 'RequestVerificationToken': aft() },
            body: formData
        })
        .then(r => r.json())
        .then(data => {
            thumb.remove();
            _uploadingCount = Math.max(0, _uploadingCount - 1);

            if (data.decision === 'rejected') {
                // Photo rejetée → ne pas ajouter, afficher le motif
                showPhotoError(`❌ Photo refusée : ${data.reason || 'non conforme à une annonce immobilière.'}`);
                return;
            }
            // approved ou review → ajouter avec badge
            addPhotoToState(data.url, file.name, data.decision || 'approved', data.reason || null);
        })
        .catch(() => {
            thumb.remove();
            _uploadingCount = Math.max(0, _uploadingCount - 1);
            showPhotoError('Erreur réseau lors de l\'envoi de la photo. Veuillez réessayer.');
        });
    }

    function showPhotoError(msg) {
        const errEl = document.getElementById('photoError');
        errEl.textContent = msg;
        errEl.style.display = 'block';
        setTimeout(() => { errEl.style.display = 'none'; }, 7000);
    }

    function addPhotoToState(url, name, decision, reason) {
        state.photos.push({ url, name: name || url, decision, reason });
        saveState();
        rebuildPhotoGrid();
    }

    function removePhoto(url) {
        state.photos = state.photos.filter(p => p.url !== url);
        saveState();
        rebuildPhotoGrid();
    }

    function rebuildPhotoGrid() {
        const grid = document.getElementById('photoGrid');
        if (!grid) return;
        grid.innerHTML = '';

        state.photos.forEach(p => {
            const decision = p.decision || 'approved';
            const badgeLabel = { approved: '✓ Validée', review: '⚠ À vérifier', rejected: '✗ Refusée' }[decision] || '✓';
            const thumbClass = { approved: '', review: 'is-review', rejected: 'is-rejected' }[decision] || '';

            const thumb = document.createElement('div');
            thumb.className = `wz-photo-thumb ${thumbClass}`;
            thumb.title = p.reason || '';
            thumb.innerHTML = `
                <img src="${escapeHtml(p.url)}" alt="Photo" loading="lazy" />
                <span class="wz-photo-badge ${decision}">${badgeLabel}</span>
                <button type="button" class="wz-photo-remove" title="Supprimer">
                    <i class="bi bi-x"></i>
                </button>`;
            thumb.querySelector('.wz-photo-remove').addEventListener('click', (e) => {
                e.stopPropagation();
                removePhoto(p.url);
            });
            grid.appendChild(thumb);
        });
    }

    function validateStep2() {
        // Bloquer si upload en cours
        if (_uploadingCount > 0) {
            showStep2Error(`${_uploadingCount} photo(s) encore en cours d'analyse. Attendez la fin de l'upload.`);
            return false;
        }
        // Bloquer si des photos sont rejetées (ne devraient pas être dans state, mais double-sécurité)
        const rejected = state.photos.filter(p => p.decision === 'rejected');
        if (rejected.length > 0) {
            showStep2Error(`${rejected.length} photo(s) refusée(s). Supprimez-les avant de continuer.`);
            return false;
        }
        // Avertir pour les photos "review" mais laisser passer
        const review = state.photos.filter(p => p.decision === 'review');
        if (review.length > 0) {
            showStep2Warning(`${review.length} photo(s) marquée(s) "À vérifier" — elles seront examinées manuellement.`);
        }
        return true;
    }

    function showStep2Error(msg) {
        removeStep2Messages();
        const el = document.createElement('div');
        el.className = 'wz-step2-error';
        el.id = 'step2-error';
        el.innerHTML = `<i class="bi bi-x-circle-fill"></i> ${escapeHtml(msg)}`;
        document.getElementById('photoGrid').insertAdjacentElement('beforebegin', el);
        setTimeout(() => el.remove(), 8000);
    }

    function showStep2Warning(msg) {
        removeStep2Messages();
        const el = document.createElement('div');
        el.className = 'wz-step2-error';
        el.id = 'step2-warning';
        el.style.cssText = 'background:#fef9c3;border-color:#fde047;color:#854d0e';
        el.innerHTML = `<i class="bi bi-exclamation-triangle-fill"></i> ${escapeHtml(msg)}`;
        document.getElementById('photoGrid').insertAdjacentElement('beforebegin', el);
        setTimeout(() => el.remove(), 6000);
    }

    function removeStep2Messages() {
        document.getElementById('step2-error')?.remove();
        document.getElementById('step2-warning')?.remove();
    }

    // ─────────────────────────────── DESCRIPTION GENERATION ──────────────
    function bindDescriptionGeneration() {
        document.getElementById('btnGenerate').addEventListener('click', generateDescription);
        document.getElementById('btnRegenerate').addEventListener('click', generateDescription);
        document.getElementById('description').addEventListener('input', e => {
            state.description = e.target.value;
            saveState();
        });
    }

    function generateDescription() {
        const loading = document.getElementById('genLoading');
        const errEl   = document.getElementById('genError');
        const btnGen  = document.getElementById('btnGenerate');
        const btnReg  = document.getElementById('btnRegenerate');
        errEl.classList.add('d-none');

        loading.classList.remove('d-none');
        btnGen.disabled = true;
        btnReg.disabled = true;

        const payload = {
            surfaceM2:          parseInt(state.surfaceM2, 10) || 0,
            nbChambres:         state.nbChambres,
            gouvernorat:        state.gouvernorat,
            delegation:         state.delegation,
            hasAscenseur:       state.hasAscenseur,
            hasBalcon:          state.hasBalcon,
            hasChauffageCentral:state.hasChauffageCentral,
            hasClimatisation:   state.hasClimatisation,
            hasGarage:          state.hasGarage,
            hasJardin:          state.hasJardin,
            hasParking:         state.hasParking,
            hasPiscine:         state.hasPiscine,
            hasTerrasse:        state.hasTerrasse,
            photos:             state.photos.map(p => p.url)
        };

        fetch('/Annonce/GenerateDescription', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': aft()
            },
            body: JSON.stringify(payload)
        })
        .then(r => r.json())
        .then(data => {
            loading.classList.add('d-none');
            btnGen.disabled = false;
            btnReg.disabled = false;

            if (data.description) {
                document.getElementById('description').value = data.description;
                state.description = data.description;
                saveState();
                btnGen.classList.add('d-none');
                btnReg.classList.remove('d-none');
            } else {
                showGenError('La génération a échoué. Réessayez ou saisissez la description manuellement.');
            }
        })
        .catch(() => {
            loading.classList.add('d-none');
            btnGen.disabled = false;
            btnReg.disabled = false;
            showGenError('Erreur réseau. Saisissez la description manuellement.');
        });
    }

    function showGenError(msg) {
        const el = document.getElementById('genError');
        el.textContent = msg;
        el.classList.remove('d-none');
    }

    // ─────────────────────────────── PRICE PREDICTION ────────────────────
    function bindPricePrediction() {
        document.getElementById('btnPredict').addEventListener('click', predictPrice);
        document.getElementById('btnUsePrice').addEventListener('click', () => {
            const val = document.getElementById('predictedPrice').dataset.raw;
            if (val) {
                document.getElementById('prix').value = val;
                state.prixTnd = val;
                saveState();
            }
        });
        document.getElementById('prix').addEventListener('input', e => {
            state.prixTnd = e.target.value;
            saveState();
        });
    }

    function predictPrice() {
        const btnPredict     = document.getElementById('btnPredict');
        const loading        = document.getElementById('predictLoading');
        const suggestion     = document.getElementById('predictSuggestion');
        const predictedPrice = document.getElementById('predictedPrice');
        const errEl          = document.getElementById('predictError');

        errEl.classList.add('d-none');
        suggestion.classList.add('d-none');
        loading.classList.remove('d-none');
        btnPredict.disabled = true;

        const payload = {
            surfaceM2:          parseInt(state.surfaceM2, 10) || 0,
            nbChambres:         state.nbChambres,
            gouvernorat:        state.gouvernorat,
            delegation:         state.delegation,
            hasAscenseur:       state.hasAscenseur,
            hasBalcon:          state.hasBalcon,
            hasChauffageCentral:state.hasChauffageCentral,
            hasClimatisation:   state.hasClimatisation,
            hasGarage:          state.hasGarage,
            hasJardin:          state.hasJardin,
            hasParking:         state.hasParking,
            hasPiscine:         state.hasPiscine,
            hasTerrasse:        state.hasTerrasse
        };

        fetch('/Annonce/PredictPrice', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': aft()
            },
            body: JSON.stringify(payload)
        })
        .then(r => r.json())
        .then(data => {
            loading.classList.add('d-none');
            btnPredict.disabled = false;

            if (data.prix_tnd) {
                const formatted = new Intl.NumberFormat('fr-TN').format(data.prix_tnd) + ' TND';
                predictedPrice.textContent = formatted;
                predictedPrice.dataset.raw = data.prix_tnd;
                suggestion.classList.remove('d-none');
            } else {
                showPredictError('La prédiction n\'a pas pu être calculée.');
            }
        })
        .catch(() => {
            loading.classList.add('d-none');
            btnPredict.disabled = false;
            // Error never blocks publishing — show inline only
            showPredictError('Erreur réseau lors de la prédiction. Vous pouvez quand même saisir le prix manuellement.');
        });
    }

    function showPredictError(msg) {
        const el = document.getElementById('predictError');
        el.textContent = msg;
        el.classList.remove('d-none');
    }

    // ─────────────────────────────── RECAP ───────────────────────────────
    function buildRecap() {
        const grid = document.getElementById('recapGrid');
        if (!grid) return;

        const equips = [
            ['hasAscenseur', 'Ascenseur'],    ['hasBalcon', 'Balcon'],
            ['hasChauffageCentral', 'Chauffage'], ['hasClimatisation', 'Climatisation'],
            ['hasGarage', 'Garage'],          ['hasJardin', 'Jardin'],
            ['hasParking', 'Parking'],        ['hasPiscine', 'Piscine'],
            ['hasTerrasse', 'Terrasse']
        ].filter(([k]) => state[k]).map(([, v]) => v);

        const rows = [
            ['Gouvernorat', escapeHtml(state.gouvernorat) || '—'],
            ['Délégation',  escapeHtml(state.delegation)  || '—'],
            ['Surface',     state.surfaceM2 ? state.surfaceM2 + ' m²' : '—'],
            ['Chambres',    state.nbChambres ?? '—']
        ];

        grid.innerHTML = rows.map(([k, v]) => `
            <div class="wz-recap-item">
                <span class="wz-recap-key">${k}</span>
                <span class="wz-recap-val">${v}</span>
            </div>`).join('');

        // Équipements
        if (equips.length > 0) {
            const badges = equips.map(e => `<span class="wz-recap-badge">${escapeHtml(e)}</span>`).join('');
            grid.insertAdjacentHTML('beforeend', `
                <div class="wz-recap-item wz-recap-full">
                    <span class="wz-recap-key">Équipements</span>
                    <div class="wz-recap-equip-list">${badges}</div>
                </div>`);
        }

        // Description
        if (state.description) {
            grid.insertAdjacentHTML('beforeend', `
                <div class="wz-recap-item wz-recap-full">
                    <span class="wz-recap-key">Description</span>
                    <span class="wz-recap-val" style="white-space:pre-line;font-weight:400">
                        ${escapeHtml(state.description.substring(0, 200))}${state.description.length > 200 ? '…' : ''}
                    </span>
                </div>`);
        }

        // Photos
        if (state.photos.length > 0) {
            const imgs = state.photos.map(p =>
                `<img class="wz-recap-thumb" src="${escapeHtml(p.url)}" alt="Photo" loading="lazy" />`
            ).join('');
            grid.insertAdjacentHTML('beforeend', `
                <div class="wz-recap-item wz-recap-full">
                    <span class="wz-recap-key">Photos (${state.photos.length})</span>
                    <div class="wz-recap-photos-row">${imgs}</div>
                </div>`);
        }
    }

    // ─────────────────────────────── PUBLISH ─────────────────────────────
    function publishAnnonce() {
        const overlay      = document.getElementById('publishOverlay');
        const spinner      = document.getElementById('publishSpinner');
        const successPanel = document.getElementById('publishSuccess');
        const errorPanel   = document.getElementById('publishErrorCard');

        overlay.classList.remove('d-none');
        spinner.classList.remove('d-none');
        successPanel.classList.add('d-none');
        errorPanel.classList.add('d-none');

        const payload = {
            prixTnd:            parseInt(state.prixTnd, 10)   || 0,
            surfaceM2:          parseInt(state.surfaceM2, 10) || 0,
            nbChambres:         state.nbChambres,
            gouvernorat:        state.gouvernorat,
            delegation:         state.delegation,
            description:        state.description,
            photos:             state.photos.map(p => p.url),
            hasAscenseur:       state.hasAscenseur,
            hasBalcon:          state.hasBalcon,
            hasChauffageCentral:state.hasChauffageCentral,
            hasClimatisation:   state.hasClimatisation,
            hasGarage:          state.hasGarage,
            hasJardin:          state.hasJardin,
            hasParking:         state.hasParking,
            hasPiscine:         state.hasPiscine,
            hasTerrasse:        state.hasTerrasse
        };

        fetch('/Annonce/Publish', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': aft()
            },
            body: JSON.stringify(payload)
        })
        .then(r => r.json())
        .then(data => {
            spinner.classList.add('d-none');
            if (data.success) {
                sessionStorage.removeItem(STORAGE_KEY);
                successPanel.classList.remove('d-none');
            } else {
                const msgs = (data.errors || ['Une erreur est survenue.']).join(' ');
                document.getElementById('publishErrorMsg').textContent = msgs;
                errorPanel.classList.remove('d-none');
            }
        })
        .catch(() => {
            spinner.classList.add('d-none');
            document.getElementById('publishErrorMsg').textContent =
                'Erreur réseau. Vérifiez votre connexion et réessayez.';
            errorPanel.classList.remove('d-none');
        });
    }

    // ─────────────────────────────── UTILITIES ───────────────────────────
    function escapeHtml(str) {
        if (!str) return '';
        return str
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#039;');
    }

    // ─────────────────────────────── INIT ────────────────────────────────
    function init() {
        loadState();
        syncFormFromState();
        showStep(state.currentStep);

        // Gouvernorat change
        document.getElementById('gouvernorat').addEventListener('change', e => {
            state.gouvernorat = e.target.value;
            state.delegation = '';
            saveState();
            populateDelegations(state.gouvernorat);
            document.getElementById('delegation').value = '';
        });

        // Délégation change
        document.getElementById('delegation').addEventListener('change', e => {
            state.delegation = e.target.value;
            saveState();
        });

        // Surface input
        document.getElementById('surface').addEventListener('input', e => {
            state.surfaceM2 = e.target.value;
            saveState();
        });

        // Chambres stepper
        document.getElementById('dec-chambres').addEventListener('click', () => {
            if (state.nbChambres > 0) {
                state.nbChambres--;
                document.getElementById('val-chambres').textContent = state.nbChambres;
                saveState();
            }
        });
        document.getElementById('inc-chambres').addEventListener('click', () => {
            state.nbChambres++;
            document.getElementById('val-chambres').textContent = state.nbChambres;
            saveState();
        });

        bindEquipmentCards();
        bindPhotoUpload();
        bindDescriptionGeneration();
        bindPricePrediction();

        // Navigation — NEXT / PUBLISH
        document.getElementById('btnNext').addEventListener('click', () => {
            const step = state.currentStep;

            if (step === 1 && !validateStep1()) return;
            if (step === 2 && !validateStep2()) return;
            if (step === 3) {
                if (!validateStep3()) return;
                publishAnnonce();
                return;
            }

            if (step < 3) showStep(step + 1);
        });

        // Navigation — BACK
        document.getElementById('btnBack').addEventListener('click', () => {
            if (state.currentStep > 1) showStep(state.currentStep - 1);
        });

        // Dismiss publish error → stay on step 3
        document.getElementById('btnDismissPublish').addEventListener('click', () => {
            document.getElementById('publishOverlay').classList.add('d-none');
        });
    }

    document.addEventListener('DOMContentLoaded', init);
})();
