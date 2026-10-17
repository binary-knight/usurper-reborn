// 1.3.4: /account/reset#<token>. The token is the only credential; it is taken from the fragment
// and cleared from the address bar at once, then posted with the new password.
(function () {
  'use strict';
  var A = window.UsurperAccount;
  var token = A.takeToken();
  var form = document.getElementById('reset-form');
  var button = document.getElementById('submit');
  var ERRORS = { password_short: 'msg-short', password_colon: 'msg-colon', password_long: 'msg-long' };

  function invalid() {
    form.hidden = true;
    A.show('msg-invalid');
    document.getElementById('msg-again').hidden = false;
  }

  if (!token) { invalid(); return; }

  form.addEventListener('submit', function (ev) {
    ev.preventDefault();
    var pw = String(document.getElementById('password').value || '');
    var again = String(document.getElementById('password2').value || '');
    // the game's rule for a new password: at least 8 characters and no colon
    if (pw.length < A.MIN_PASSWORD) { A.show('msg-short'); return; }
    if (pw.length > A.MAX_PASSWORD) { A.show('msg-long'); return; }
    if (pw.indexOf(':') !== -1) { A.show('msg-colon'); return; }
    if (pw !== again) { A.show('msg-mismatch'); return; }
    button.disabled = true;
    A.show('msg-busy');
    A.post('/api/account/reset', { token: token, password: pw, lang: A.lang }).then(function (r) {
      if (r.status === 200 && r.data.ok) {
        token = '';
        form.hidden = true;
        A.show('msg-done');
        document.getElementById('msg-bbs').hidden = false;
        return;
      }
      button.disabled = false;
      if (r.data.error === 'invalid_link') { invalid(); return; }
      A.show(ERRORS[r.data.error] || 'msg-error');
    }, function () {
      button.disabled = false;
      A.show('msg-error');
    });
  });
})();
