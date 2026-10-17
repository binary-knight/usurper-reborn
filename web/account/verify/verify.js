// 1.3.4: /account/verify#<token>. The address is confirmed only when the visitor presses the button,
// so a mail scanner that opens the link changes nothing.
(function () {
  'use strict';
  var A = window.UsurperAccount;
  var token = A.takeToken();
  var button = document.getElementById('confirm');
  var intro = document.getElementById('intro');

  function invalid() {
    button.hidden = true;
    intro.hidden = true;
    A.show('msg-invalid');
  }

  if (!token) { invalid(); return; }

  button.addEventListener('click', function () {
    button.disabled = true;
    A.show('msg-busy');
    A.post('/api/account/verify', { token: token, lang: A.lang }).then(function (r) {
      if (r.status === 200 && r.data.ok) {
        token = '';
        button.hidden = true;
        intro.hidden = true;
        A.show('msg-done');
        return;
      }
      button.disabled = false;
      if (r.data.error === 'invalid_link') { invalid(); return; }
      A.show('msg-error');
    }, function () {
      button.disabled = false;
      A.show('msg-error');
    });
  });
})();
