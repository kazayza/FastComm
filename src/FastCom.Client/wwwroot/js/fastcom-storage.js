/* ==========================================================================
   FastCom — تخزين الـ Token في localStorage
   --------------------------------------------------------------------------
   🔴 ملاحظة أمان: في Blazor WASM أي حاجة في الـ Browser ممكن تتقرا.
      عشان كده الحماية الحقيقية كلها على الـ Server.
   ========================================================================== */
window.fastcomStore = {

    set: function (key, value) {
        try {
            localStorage.setItem(key, value);
        } catch (e) {
            console.warn('fastcomStore.set failed:', e);
        }
    },

    get: function (key) {
        try {
            return localStorage.getItem(key);
        } catch (e) {
            console.warn('fastcomStore.get failed:', e);
            return null;
        }
    },

    remove: function (key) {
        try {
            localStorage.removeItem(key);
        } catch (e) {
            console.warn('fastcomStore.remove failed:', e);
        }
    },

    /* بيمسح كل مفاتيح FastCom — مفيد لو حصل تلف في البيانات المخزنة */
    clearAll: function () {
        try {
            Object.keys(localStorage)
                .filter(function (k) { return k.indexOf('fastcom.') === 0; })
                .forEach(function (k) { localStorage.removeItem(k); });
        } catch (e) {
            console.warn('fastcomStore.clearAll failed:', e);
        }
    }
};
