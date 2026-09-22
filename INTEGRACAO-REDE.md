# Integração de rede — MVP de laboratório

Implementado para ASP.NET Core MVC/.NET 10, MySQL e MikroTik RouterOS 7 HotSpot + REST. Não requer API de autorização nos APs Huawei/Wavlink. Não é configuração pronta para a rede de produção da Arena.

## Estado seguro inicial

`Network:Enabled` é falso por padrão. As novas cobranças ficam bloqueadas até configurar a rede. Login, cadastro e catálogo continuam disponíveis. A migração não é automática. Os registros antigos de access_sessions permanecem; sua tela agora esclarece que não comprovam liberação no roteador.

## Fluxo implementado

1. Cliente na rede de visitantes abre `/rede/entrada` por HTTPS.
2. O servidor consulta a tabela `/ip/hotspot/host`, correlaciona o IPv4 remoto com um único MAC do HotSpot configurado e grava cookie protegido. Não aceita um MAC arbitrário por query string.
3. Login/cadastro continuam no AccountController. O checkout preserva o contexto protegido e verifica novamente o dispositivo antes da cobrança.
4. O pedido é persistido antes de POST `/v1/payments`: usuário, plano/preço/duração congelados, gateway e MAC. A referência e chave de idempotência são o ID do pedido. O payload é protegido com ASP.NET Data Protection para permitir repetição idêntica após timeout, e é descartado quando o pagamento é identificado.
5. POST `/webhooks/mercadopago` valida HMAC de x-signature usando data.id da query. O corpo não decide a autorização. Após assinatura válida, grava uma notificação durável antes de responder 200.
6. O worker consulta GET `/v1/payments/{id}`. Compara pedido, preço, moeda BRL, recebedor e ambiente. Apenas approved pode provisionar. Pendências e falhas são repetidas, sem depender do navegador permanecer aberto.
7. Cria usuário com nome exclusivo do pedido, senha aleatória, MAC, server/profile e limit-uptime. Um pedido de 60 minutos envia 60m. Repetições reconciliam o usuário existente; não alteram contadores. Nome existente com atributos divergentes interrompe a liberação.
8. A tela de acompanhamento distingue aprovação, preparação e conexão. O cliente confirma o login por formulário HTTPS no servlet oficial do HotSpot. As credenciais entregues ao cliente pertencem somente à compra; credenciais administrativas nunca saem do backend.
9. O RouterOS encerra o acesso ao consumir o limite. O banco não tenta simular esse corte com expires_at. Refunded, charged_back e cancelled recebidos e confirmados provocam desativação do usuário e remoção das sessões ativas.

## Configuração do laboratório

Use o arquivo `appsettings.Network.example.json` somente como referência: ele NÃO é carregado automaticamente. Configure os campos Network e MercadoPago:WebhookSecret por variáveis de ambiente, secrets de desenvolvimento ou configuração local não versionada. Em variáveis de ambiente, `Network__Enabled` equivale a `Network:Enabled`.

Campos obrigatórios: GatewayId, RestUrl HTTPS terminado em `/rest/`, Username, Password, HotspotServer, UserProfile, LoginUrl HTTPS do servlet `/login`, ClientSubnet IPv4/CIDR, WebhookUrl pública HTTPS, CollectorId correspondente à conta do pagamento, LiveMode explícito. Também configurar AccessToken, PublicKey e WebhookSecret do Mercado Pago.

Em desenvolvimento, usar credenciais e meios de teste do Mercado Pago e LiveMode=false. Não inserir segredos no arquivo exemplo nem no Git. A integração não desativa a validação TLS; instale a CA adequada para os certificados do laboratório. Preserve as chaves de ASP.NET Data Protection entre reinícios e compartilhe/proteja o key ring se houver mais de uma instância; sua perda impede recuperar cookies, senhas HotSpot e payloads pendentes.

Aplicar manualmente `scriptsDB/006_network_orders.sql` no banco de desenvolvimento, após os scripts anteriores, com cópia de segurança. O script não contém USE para evitar selecionar automaticamente o banco de produção. Acrescenta somente network_orders e payment_notifications. O schema guarda o snapshot do pedido em documento JSON serializado e índices próprios para IDs/usuário; não altera a tabela histórica.

## Rede e confiança no dispositivo

Este MVP exige que a conexão do cliente ao portal chegue com seu IPv4 real, sem NAT ou proxy intermediário. `ClientSubnet` deve ser a rede de visitantes, e não uma rede pública ou de gestão. Não há middleware que confie indiscriminadamente em X-Forwarded-For. Uma hospedagem externa não atende a essa premissa e precisa de adaptação com componente local autenticado; não basta encaminhar o MAC em uma URL.

A rede deve usar DHCP/controlar conflitos e isolar clientes para reduzir falsificação de IP/MAC. MAC não é uma identidade criptográfica e pode mudar por recursos de privacidade do dispositivo. O pedido vale para o MAC observado, não para todo dispositivo da conta. Para trocar de dispositivo, será necessária uma política explícita de transferência.

No RouterOS: habilitar HotSpot no modo do equipamento, instalar certificado válido, configurar login por HTTPS e perfil de usuário com shared-users=1. Servidor/profile precisam existir antes da primeira compra. Revisar limites adicionais do perfil para não encerrar sessões indevidamente. A conta REST deve ter somente as permissões administrativas necessárias e acesso permitido exclusivamente pela rede de gestão. Não habilitar bypassed para clientes pagos. Não habilitar trial automaticamente.

A página de login do HotSpot deve oferecer um link HTTPS fixo para `https://SEU-PORTAL/rede/entrada`. O backend resolve o MAC consultando o roteador; não é necessário aceitar as variáveis MAC/IP do HTML como prova. Depois da aprovação, o formulário usa exclusivamente Network:LoginUrl, nunca um endereço escolhido pelo visitante.

Permitir no walled garden o portal e as dependências efetivamente usadas pelo checkout. Acesso a aplicativos bancários não está automaticamente resolvido: testar os bancos desejados ou usar dados móveis/outro dispositivo. Uma franquia pré-pagamento é uma funcionalidade separada e ainda não foi implementada. Impedir saída IPv6 que contorne o controle IPv4 do HotSpot.

## Duração

Interpretação adotada: minutos de uso conectado, acumulados na conta HotSpot da compra. Não consome enquanto aguarda pagamento ou antes da primeira autenticação. Não é uma validade contínua por relógio; desconexões permitem usar saldo restante. Não apagar/recriar usuários ou resetar uptime para recuperar uma falha. Se um usuário já marcado ready desaparecer, o worker exige revisão em vez de recriá-lo automaticamente.

É obrigatório validar persistência dos contadores após reinício e queda de energia no RouterOS escolhido antes de produção. Esta implementação não afirma que uma falha de armazenamento do equipamento preserva todo o consumo. Para validade contínua e política centralizada, evoluir para RADIUS com Session-Timeout calculado pelo saldo/validade e recusa de novos logins após expiração.

## Recuperação e limites desta primeira versão

- Pedidos são coordenados por GET_LOCK do MySQL, com liberação explícita em conexão dedicada. Todos os processos devem usar o mesmo banco/servidor para essa coordenação. Não presume locks distribuídos entre servidores MySQL independentes.
- Aprovação e enfileiramento da resposta síncrona são gravados na mesma transação. Webhooks têm contador de revisão para não apagar uma notificação mais nova durante processamento.
- O worker processa até 20 notificações por rodada, repetindo falhas após 30 segundos. Não é dimensionamento para toda a Arena; monitorar logs/fila e ajustar após teste de carga. Divergências de pagamento geram log de erro e não concedem acesso.
- A assinatura não aplica uma janela temporal arbitrária que descarte reentregas legítimas. Replays válidos consultam o estado atual do pagamento e não reinicializam o acesso. A API pública precisa de limitação de requisições na implantação.
- Falha permanente ao criar um pagamento pode exigir atendimento: o sistema preserva o pedido/payload para não gerar uma segunda cobrança após resultado incerto. Não cria automaticamente outra compra em erro de transporte.
- O acompanhamento de um pedido exige autenticação do proprietário. Guarde sua URL. Histórico navegável de todos os pedidos e interface de atendimento/reprocessamento ainda não fazem parte desta versão.
- Desativar Network:Enabled interrompe novas cobranças e processamento; não revoga automaticamente sessões existentes. Seu limite já está no roteador.
- Estorno depende de notificação/reconciliação; não existe varredura periódica completa de pagamentos finalizados caso o provedor deixe de entregar todas as notificações. Uma confirmação é refeita ao solicitar o formulário de conexão.

## Validação

Para executar os testes incluídos: `dotnet run --project tests/NetworkIntegration/Network.Tests.csproj`. São testes executáveis sem framework/NuGet adicional; o processo falha com código não zero se uma verificação falhar.

Opcionalmente configure `Network:DataProtectionKeyPath` com diretório persistente fora do repositório, acessível somente à conta do serviço. No Windows, o código aplica DPAPI para proteção em repouso; em outros sistemas, providencie proteção adequada do key ring antes de produção. A configuração não altera o diretório padrão se estiver vazia.

Projeto compilado em .NET SDK 10.0.400 sem avisos/erros. Testes de lógica/transporte HTTP simulado cobrem assinatura, adulteração, preço/moeda/recebedor/ambiente, origem fora da rede, MAC ambíguo, timeout após criação, reconciliação sem novo crédito e esgotamento do limite. Não foram usados roteador, banco ou gateway de pagamento reais nesses testes.

Antes de ativar: testar a migração em MySQL isolado, concorrência entre instâncias, reentrega de webhook, pagamento PIX/cartão de teste, reconexão, corte com backend desligado, reinício do roteador, certificados, walled garden e dispositivos Android/iOS. Fazer ensaio em CHR com poucos minutos, seguido de plano de uma hora.

## Documentação oficial usada

- REST RouterOS, métodos GET/PUT/PATCH/DELETE e HTTPS: https://help.mikrotik.com/docs/spaces/ROS/pages/47579162/REST%2BAPI
- HotSpot, MAC, limit-uptime, shared-users, tabelas e bloqueio IPv4: https://manual.mikrotik.com/docs/authentication-authorization-accounting/hotspot-captive-portal/
- Retorno ao servlet oficial de login: https://help.mikrotik.com/docs/spaces/ROS/pages/87162881/Hotspot%2Bcustomisation
- HMAC Mercado Pago: https://www.mercadopago.com.mx/developers/en/docs/prestashop/additional-content/your-integrations/notifications/webhooks
- Webhooks e consulta de pagamento: https://www.mercadopago.com.br/developers/en/docs/checkout-bricks/additional-content/your-integrations/notifications/webhooks
- RADIUS: https://manual.mikrotik.com/docs/authentication-authorization-accounting/radius/
- CHR oficial: https://help.mikrotik.com/docs/spaces/ROS/pages/18350234/Cloud%2BHosted%2BRouter%2BCHR

Não foram enviados comandos aos APs Huawei, Wavlink ou switch. O laboratório CHR reproduz a lógica RouterOS, não rádio/PoE/desempenho físico ou o licenciamento L4 do RB760iGS.
