export async function init() {
    if (!window.SwaggerUIBundle) {
        await new Promise((resolve, reject) => {
            const s = document.createElement('script');
            s.src = 'https://unpkg.com/swagger-ui-dist@5.31.2/swagger-ui-bundle.js';
            s.integrity = 'sha384-sj6CZpymZ+xP88NMFJ+Av913YaSOGICRkDI1J8LvHiOrnlExnAIR37b6qV3R8wLG';
            s.crossOrigin = 'anonymous';
            s.onload = resolve;
            s.onerror = reject;
            document.head.appendChild(s);
        });
    }

    SwaggerUIBundle({
        url: '/api-docs/swagger.json',
        dom_id: '#swagger-ui',
        presets: [SwaggerUIBundle.presets.apis],
        persistAuthorization: true,
        defaultModelsExpandDepth: -1,
        tryItOutEnabled: true
    });
}

export async function copyToClipboard(text) {
    try {
        await navigator.clipboard.writeText(text);
        return true;
    } catch {
        return false;
    }
}
