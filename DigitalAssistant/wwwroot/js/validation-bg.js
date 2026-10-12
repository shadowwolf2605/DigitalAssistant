// Съобщения на български за проверките в браузъра
$.extend($.validator.messages, {
    required: "Полето е задължително.",
    email: "Въведете правилен имейл адрес.",
    url: "Въведете правилен адрес на страница.",
    date: "Въведете правилна дата.",
    number: "Въведете число.",
    digits: "Въведете цяло число.",
    equalTo: "Стойностите не съвпадат.",
    maxlength: $.validator.format("Въведете най-много {0} знака."),
    minlength: $.validator.format("Въведете поне {0} знака."),
    range: $.validator.format("Въведете стойност между {0} и {1}."),
    max: $.validator.format("Въведете стойност най-много {0}."),
    min: $.validator.format("Въведете стойност поне {0}."),
    step: function (p) { return p == 1 ? "Въведете цяло число." : $.validator.format("Въведете стойност, кратна на {0}.", p); }
});

// Махаме грешката веднага щом полето се попълни (иначе бутонът се мести при клик и кликът се губи)
$(function () {
    $('form').on('input change', 'input, select, textarea', function () {
        var v = $(this.form).data('validator');
        if (v && this.name && (this.name in v.submitted || this.name in v.invalid)) v.element(this);
    });
});
