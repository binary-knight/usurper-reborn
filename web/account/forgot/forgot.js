// 1.3.4: /account/forgot. One reply for every name or address; the server sends a link only to an
// account with a confirmed address.
(function () {
  'use strict';
  var A = window.UsurperAccount;
  var form = document.getElementById('forgot-form');
  var input = document.getElementById('name');
  var button = document.getElementById('submit');
  form.addEventListener('submit', function (ev) {
    ev.preventDefault();
    var name = String(input.value || '').trim();
    if (!name) { A.show('msg-empty'); return; }
    button.disabled = true;
    A.show('msg-busy');
    A.post('/api/account/forgot', { name: name, lang: A.lang }).then(function (r) {
      button.disabled = false;
      // a server that does not send mail says so, the same answer for every name
      if (r.data.error === 'mail_off') { A.show('msg-mail-off'); return; }
      if (r.status === 200) { input.value = ''; A.show('msg-sent'); } else { A.show('msg-error'); }
    }, function () {
      button.disabled = false;
      A.show('msg-error');
    });
  });
})();
