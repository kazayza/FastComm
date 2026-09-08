/* ═══════════════════════════════════════════════════════════════════
   فاست كوم — مساعدات المتصفح
   ═══════════════════════════════════════════════════════════════════ */
window.fastcom = {

    /**
     * بينزّل ملف من الـ API عن طريق fetch + blob.
     * 🔴 لازم تمرّر الـ JWT في الباراميتر التالت — fetch عادي مش بيعرف
     *    حاجة عن الـ AuthorizationMessageHandler بتاع Blazor.
     */
    downloadFile: async function (url, fileName, jwt) {
        // 🔴 الـ JWT **مش** بيتحط أوتوماتيك — fetch عادي مايعرفش حاجة عن
        //    الـ AuthorizationMessageHandler بتاع Blazor. لازم نمرّر التوكن.
        var headers = {};
        if (jwt) { headers['Authorization'] = 'Bearer ' + jwt; }
        var res = await fetch(url, { credentials: 'same-origin', headers: headers });
        if (!res.ok) {
            var text = '';
            try { text = await res.text(); } catch (e) { }
            throw new Error(text || ('HTTP ' + res.status));
        }
        var blob = await res.blob();
        var a = document.createElement('a');
        var href = URL.createObjectURL(blob);
        a.href = href;
        a.download = fileName || 'download';
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        setTimeout(function () { URL.revokeObjectURL(href); }, 2000);
        return true;
    },

    /** بينزّل ملف من base64 — مفيد لما الـ bytes جات عبر HttpClient (JWT متحطّ). */
    downloadBase64: function (base64, fileName, mime) {
        var bin = atob(base64);
        var len = bin.length;
        var bytes = new Uint8Array(len);
        for (var i = 0; i < len; i++) { bytes[i] = bin.charCodeAt(i); }
        var blob = new Blob([bytes], { type: mime || 'application/octet-stream' });
        var a = document.createElement('a');
        var href = URL.createObjectURL(blob);
        a.href = href;
        a.download = fileName || 'download';
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        setTimeout(function () { URL.revokeObjectURL(href); }, 2000);
        return true;
    },

    /** بيفتح نافذة الطباعة — CSS الطباعة بيخفي القائمة والـ Header. */
    printPage: function () {
        window.print();
        return true;
    },

    /** بينسخ نص للـ clipboard — بيرجع false لو المتصفح رفض. */
    copyText: async function (text) {
        try {
            await navigator.clipboard.writeText(text);
            return true;
        } catch (e) {
            return false;
        }
    }
};
