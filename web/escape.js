// Shared escaping helpers for the web pages (index, steam, dashboard, admin, balance).
// Pages load it with <script src="/escape.js"></script> before their own script.
// Rule for page code: every value that comes from the server or a third party is
// passed through escapeHtml before it goes into an HTML string, and inline event
// handlers never carry data (use data-* attributes and one delegated listener).
(function (root) {
  'use strict';

  var ENTITIES = {
    '&': '&amp;',
    '<': '&lt;',
    '>': '&gt;',
    '"': '&quot;',
    "'": '&#39;',
    '\\': '&#92;'
  };

  // Escapes & < > " ' and backslash, so the result is safe in element text and in
  // a quoted attribute value. null and undefined become an empty string; anything
  // else (numbers included) is converted with String().
  function escapeHtml(value) {
    if (value === null || value === undefined) return '';
    return String(value).replace(/[&<>"'\\]/g, function (c) { return ENTITIES[c]; });
  }

  // Returns the normalised URL when value is an absolute https: URL, otherwise null.
  // Use it for any third-party href or src; never put a URL that fails it in markup.
  function safeHttpsUrl(value) {
    if (typeof value !== 'string' || value.length === 0) return null;
    var parsed;
    try {
      parsed = new URL(value);
    } catch (e) {
      return null;
    }
    return parsed.protocol === 'https:' ? parsed.href : null;
  }

  var api = { escapeHtml: escapeHtml, safeHttpsUrl: safeHttpsUrl };
  root.UsurperEscape = api;
  root.escapeHtml = escapeHtml;
  root.safeHttpsUrl = safeHttpsUrl;
  if (typeof module === 'object' && module && module.exports) module.exports = api;
})(typeof globalThis !== 'undefined' ? globalThis : this);
