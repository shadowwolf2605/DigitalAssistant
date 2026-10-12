// Списък с избраните файлове и проверка преди изпращане (т. 3.1.3)
(function () {
    const input = document.getElementById('Files');
    if (!input) return;
    const zone = document.getElementById('dropZone');
    const list = document.getElementById('fileList');
    const error = document.getElementById('fileError');
    const allowed = ['pdf', 'jpg', 'jpeg', 'png', 'doc', 'docx'];
    const maxFiles = 5, maxSize = 5 * 1024 * 1024;

    function size(b) { return b >= 1048576 ? (b / 1048576).toFixed(1) + ' MB' : Math.max(1, Math.round(b / 1024)) + ' KB'; }

    function problems() {
        const files = Array.from(input.files);
        const errors = [];
        if (files.length > maxFiles) errors.push('Може да прикачите най-много ' + maxFiles + ' файла.');
        files.forEach(f => {
            const ext = f.name.split('.').pop().toLowerCase();
            if (!allowed.includes(ext)) errors.push('„' + f.name + '“: разрешени са само файлове .pdf, .jpg, .png, .doc и .docx.');
            else if (f.size > maxSize) errors.push('„' + f.name + '“ е по-голям от 5 MB.');
            else if (f.size === 0) errors.push('„' + f.name + '“ е празен файл.');
        });
        return errors;
    }

    function render() {
        list.innerHTML = '';
        Array.from(input.files).forEach((f, i) => {
            const ext = f.name.split('.').pop().toLowerCase();
            const bad = !allowed.includes(ext) || f.size > maxSize || f.size === 0;
            const li = document.createElement('li');
            li.className = 'anim' + (bad ? ' bad' : '');
            li.style.setProperty('--i', i);
            const icon = ext === 'pdf' ? 'bi-file-earmark-pdf' : (['jpg', 'jpeg', 'png'].includes(ext) ? 'bi-file-earmark-image' : 'bi-file-earmark-text');
            li.innerHTML = '<i class="bi ' + (bad ? 'bi-exclamation-triangle' : icon) + '" aria-hidden="true"></i>';
            li.append(document.createTextNode(f.name));
            const s = document.createElement('span');
            s.className = 'size';
            s.textContent = size(f.size);
            li.append(s);
            list.append(li);
        });
        error.textContent = problems().join(' ');
    }

    input.addEventListener('change', render);
    ['dragenter', 'dragover'].forEach(e => zone.addEventListener(e, () => zone.classList.add('over')));
    ['dragleave', 'drop'].forEach(e => zone.addEventListener(e, () => zone.classList.remove('over')));

    // Адресът се иска само при получаване по пощата
    const box = document.getElementById('addressBox');
    const radios = document.querySelectorAll('input[name=ResultDelivery]');
    const byPost = () => Array.from(radios).some(r => r.checked && r.value === 'по пощата');
    if (box) {
        radios.forEach(r => r.addEventListener('change', () => { box.hidden = !byPost(); }));
        box.hidden = !byPost();
    }

    // Всичко се проверява тук, преди изпращане, защото след грешка от сървъра браузърът не пази избраните файлове
    input.form.addEventListener('submit', ev => {
        const errors = problems();
        // При data-optional (отговорът на служителя) може и без файлове
        if (input.files.length === 0 && !input.hasAttribute('data-optional')) errors.unshift('Прикачете поне един документ.');
        let addressMissing = false;
        if (box) {
            const address = box.querySelector('input');
            const message = document.getElementById('addressError');
            addressMissing = byPost() && !address.value.trim();
            address.classList.toggle('is-invalid', addressMissing);
            if (message) message.textContent = addressMissing ? 'Въведете адрес за получаване по пощата.' : '';
        }
        if (errors.length || addressMissing) {
            ev.preventDefault();
            error.textContent = errors.join(' ');
            (errors.length ? zone : box).scrollIntoView({ behavior: 'smooth', block: 'center' });
        }
    });
})();
