/*
 * Gallery Optimizer - gobernador de memoria del carrusel.
 *
 * Mantiene acotado el numero de imagenes que el cliente (webOS, Tizen,
 * navegadores antiguos) conserva decodificadas en memoria. Cada bitmap
 * decodificado de una foto a 1920x1080 ocupa ~8 MB en RGBA, de modo que
 * retener unas pocas decenas agota el heap del televisor y la app se
 * reinicia.
 *
 * Estrategia:
 *  - Se observan los <img> del documento (y los que se vayan creando).
 *  - Las imagenes que salen del viewport o que quedan fuera del DOM
 *    (Swiper las guarda en su cache de slides virtuales) se sustituyen por
 *    un pixel transparente: el navegador libera el bitmap.
 *  - Se conservan como maximo LIMIT imagenes fuera de pantalla (politica
 *    LRU); las visibles nunca se liberan.
 *  - Al volver a estar visibles se restaura su URL original.
 *
 * Los marcadores de configuracion los sustituye el servidor al servir el
 * fichero (ver CarouselScriptBuilder).
 */
(function () {
    'use strict';

    if (window.__galleryOptimizerActive) {
        return;
    }
    window.__galleryOptimizerActive = true;

    var LIMIT = __LIMIT__;
    var PX = '__PLACEHOLDER__';
    var ROOT_MARGIN = '100%';

    var states = new WeakMap();
    var loaded = [];
    var evictPending = false;
    var observer = null;

    function stateOf(el) {
        var s = states.get(el);
        if (!s) {
            /* Se asume visible hasta que el observer diga lo contrario: es mas
               seguro no liberar algo que aun no se ha medido. */
            s = { visible: true, full: null, released: false };
            states.set(el, s);
        }
        return s;
    }

    /* Element.isConnected no existe en navegadores antiguos (webOS 3.x). */
    function inDocument(el) {
        if (typeof el.isConnected === 'boolean') {
            return el.isConnected;
        }
        return document.documentElement.contains(el);
    }

    function absolute(url) {
        if (!url || url.indexOf('data:') === 0 || url.indexOf('blob:') === 0) {
            return null;
        }
        try {
            return new URL(url, document.baseURI).href;
        } catch (e) {
            return null;
        }
    }

    function touch(el) {
        var i = loaded.indexOf(el);
        if (i !== -1) {
            loaded.splice(i, 1);
        }
        loaded.push(el);
    }

    function forget(el) {
        var i = loaded.indexOf(el);
        if (i !== -1) {
            loaded.splice(i, 1);
        }
    }

    function release(el) {
        var s = stateOf(el);
        if (s.released || !s.full) {
            return;
        }
        s.released = true;
        forget(el);
        el.src = PX;
    }

    function restore(el) {
        var s = stateOf(el);
        if (!s.full) {
            return;
        }
        if (s.released) {
            s.released = false;
            el.src = s.full;
        }
        touch(el);
    }

    function evict() {
        evictPending = false;

        var snapshot = loaded.slice();
        var hidden = 0;
        var i;
        var el;
        var s;

        for (i = 0; i < snapshot.length; i++) {
            el = snapshot[i];
            s = states.get(el);
            if (!s) {
                continue;
            }
            if (!inDocument(el)) {
                /* Fuera del DOM: nunca se esta pintando, se libera siempre. */
                release(el);
            } else if (!s.visible && !s.released) {
                hidden++;
            }
        }

        for (i = 0; i < snapshot.length && hidden > LIMIT; i++) {
            el = snapshot[i];
            s = states.get(el);
            if (s && inDocument(el) && !s.visible && !s.released) {
                release(el);
                hidden--;
            }
        }
    }

    function scheduleEvict() {
        if (evictPending) {
            return;
        }
        evictPending = true;
        setTimeout(evict, 0);
    }

    function watch(el) {
        if (!el || el.tagName !== 'IMG') {
            return;
        }

        var s = stateOf(el);
        var src = absolute(el.getAttribute('src'));
        if (!src || src === PX) {
            return;
        }

        if (s.full !== src) {
            s.full = src;
            s.released = false;
            touch(el);
        }

        if (el.decoding !== 'async') {
            el.decoding = 'async';
        }

        if (observer) {
            observer.observe(el);
        }
    }

    function scan(root) {
        if (!root || root.nodeType !== 1) {
            return;
        }
        if (root.tagName === 'IMG') {
            watch(root);
            return;
        }
        var imgs = root.querySelectorAll('img');
        for (var i = 0; i < imgs.length; i++) {
            watch(imgs[i]);
        }
    }

    function isNear(el) {
        var rect = el.getBoundingClientRect();
        var height = window.innerHeight || 0;
        return rect.bottom > -height && rect.top < height * 2;
    }

    /* Ruta alternativa para navegadores sin IntersectionObserver
       (webOS 3.x / Chromium 38). Solo recorre las imagenes ya conocidas. */
    function refresh() {
        var imgs = document.querySelectorAll('img');
        for (var i = 0; i < imgs.length; i++) {
            var s = states.get(imgs[i]);
            if (!s || !s.full) {
                continue;
            }
            s.visible = inDocument(imgs[i]) && isNear(imgs[i]);
            if (s.visible) {
                restore(imgs[i]);
            }
        }
        evict();
    }

    if (typeof IntersectionObserver === 'function') {
        observer = new IntersectionObserver(function (entries) {
            for (var i = 0; i < entries.length; i++) {
                var el = entries[i].target;
                var s = stateOf(el);
                s.visible = entries[i].isIntersecting;
                if (s.visible) {
                    restore(el);
                }
            }
            scheduleEvict();
        }, { rootMargin: ROOT_MARGIN });
    } else {
        var timer = 0;
        var scheduleRefresh = function () {
            clearTimeout(timer);
            timer = setTimeout(refresh, 300);
        };
        window.addEventListener('scroll', scheduleRefresh, true);
        window.addEventListener('resize', scheduleRefresh);
        setTimeout(refresh, 1000);
    }

    if (typeof MutationObserver === 'function') {
        new MutationObserver(function (mutations) {
            for (var i = 0; i < mutations.length; i++) {
                var added = mutations[i].addedNodes;
                for (var j = 0; j < added.length; j++) {
                    scan(added[j]);
                }
            }
        }).observe(document.documentElement, { childList: true, subtree: true });
    }

    /* La vista puede cambiar el src (Swiper crea slides nuevas): se refresca
       la URL "completa" asociada al elemento cuando termina la carga. */
    document.addEventListener('load', function (event) {
        var el = event.target;
        if (!el || el.tagName !== 'IMG') {
            return;
        }
        var s = states.get(el);
        if (s && s.released && absolute(el.getAttribute('src')) === null) {
            /* Es la carga del pixel de relleno que pusimos nosotros. */
            return;
        }
        watch(el);
    }, true);

    /* Al cerrar o abandonar la vista se libera todo de golpe. */
    window.addEventListener('pagehide', function () {
        var snapshot = loaded.slice();
        for (var i = 0; i < snapshot.length; i++) {
            release(snapshot[i]);
        }
    });

    scan(document.documentElement);
})();
