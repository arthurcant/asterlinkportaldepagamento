# Integração com a REST API oficial do RouterOS 7

O portal usa a REST API para identificar o dispositivo, criar a conta HotSpot, conectar o cliente e revogar o acesso. As credenciais administrativas permanecem no backend. O navegador não recebe senhas do roteador nem precisa enviar um formulário de login ao HotSpot.

## Configuração centralizada

Configure `appsettings.Development.json`, carregado automaticamente no ambiente `Development`. O arquivo está ignorado pelo Git porque contém credenciais locais. Os nomes de parâmetros definidos pelo protocolo ficam no serviço; endereços, credenciais e opções da instalação ficam no JSON.

| Campo em `Network` | Finalidade |
| --- | --- |
| `Enabled` | Ativa novas cobranças integradas e o processamento da fila. |
| `RestUrl` | Endereço do roteador terminado em `/rest/`. |
| `Username`, `Password` | Credenciais administrativas enviadas com HTTP Basic Auth. |
| `HotspotServer` | Nome real do servidor em `/ip/hotspot`. |
| `UserProfile` | Perfil existente em `/ip/hotspot/user/profile`, com `shared-users=1`. |
| `ClientSubnet` | Rede IPv4/CIDR dos clientes, usada para validar a origem. |
| `GatewayId` | Identificador persistido nos pedidos. Não trocar enquanto houver pedidos em uso. |
| `RequestTimeoutSeconds` | Tempo máximo de cada requisição REST, entre 1 e 60 segundos. |
| `AllowInsecureHttpForDevelopment` | Permite HTTP somente no ambiente Development. Em outros ambientes a aplicação recusa essa opção. |

O endereço e as credenciais fornecidas foram aplicados ao JSON local. Os valores preexistentes `HotspotServer=hotspot1`, `UserProfile=asterlink-paid-lab`, `ClientSubnet=192.168.56.0/24` e `GatewayId=arena-lab` foram preservados; **não foram confirmados no equipamento**. A API em `192.168.1.75` não respondeu à consulta durante esta alteração. A rede dos clientes não pode ser deduzida do IP de gerenciamento do roteador.

Mantida a URL HTTP já utilizada no desenvolvimento. HTTP transmite as credenciais sem criptografia: utilizar somente em uma rede de laboratório confiável. Para HTTPS, habilitar `www-ssl` com certificado confiável e alterar `RestUrl`; a aplicação não ignora certificados inválidos. REST usa `www`/`www-ssl`, e não as portas 8728/8729 da API binária.

`ConnectionStrings:DefaultConnection` e a seção `MercadoPago` continuam necessários para persistir pedidos e confirmar pagamentos. Removê-los impediria a liberação após pagamento. As opções `WebhookUrl`, `CollectorId` e `LiveMode` foram movidas de `Network` para `MercadoPago`, no mesmo arquivo.

Ainda é necessário preencher com dados reais:

- `MercadoPago:WebhookUrl`: URL HTTPS pública que recebe `/webhooks/mercadopago`.
- `MercadoPago:WebhookSecret`: segredo de assinatura configurado no Mercado Pago.
- `MercadoPago:CollectorId`: ID da conta recebedora correspondente ao token.

As credenciais de pagamento existentes foram preservadas. Campos desconhecidos permanecem vazios; a aplicação informa a configuração pendente e não inicia a cobrança. Em outro ambiente, forneça as mesmas seções por configuração apropriada ao ambiente; o arquivo Development não é carregado automaticamente em Production.

## Fluxo após o pagamento

1. O dispositivo abre `/rede/entrada`. O backend correlaciona seu IPv4 remoto com um único MAC na tabela `/ip/hotspot/host` e protege o contexto em cookie.
2. O checkout revalida o dispositivo e grava um pedido com preço e duração do plano obtidos no banco. Valores enviados pelo navegador não definem o tempo comprado.
3. A resposta do pagamento e as notificações assinadas alimentam uma fila persistente. A conciliação consulta o pagamento no Mercado Pago e confere ID, referência, valor, moeda, recebedor e ambiente.
4. Somente `approved` cria um usuário via `PUT /rest/ip/hotspot/user`, com senha exclusiva da compra, MAC, servidor, perfil e `limit-uptime`. Um plano de 90 minutos envia `90m`.
5. O backend resolve novamente o IP atual do MAC e executa `POST /rest/ip/hotspot/active/login` com `ip`, `mac-address`, `user` e `password`. Depois consulta `/ip/hotspot/active` para confirmar a conexão.
6. Se o cliente estiver temporariamente ausente, a fila tenta novamente. A página também oferece uma tentativa manual pelo backend. Login e senha não são enviados ao navegador.
7. O RouterOS encerra o acesso quando o usuário atinge `limit-uptime`. Pagamentos estornados, contestados ou cancelados, confirmados pela conciliação, desativam o usuário com `PATCH` e removem sessões ativas com `DELETE`.

Notificações repetidas e respostas perdidas não recriam o usuário, não zeram contadores e não reiniciam uma sessão já ativa. A criação é persistida antes do login. Uma conta marcada como pronta que desapareça do roteador exige revisão, em vez de ganhar outra franquia automaticamente.

## Duração e requisitos da rede

A duração representa **minutos acumulados de conexão**, como já informado na tela do plano. Não é validade corrida desde a confirmação do pagamento. Desconectar preserva o saldo restante; a compra expirada não ganha novos minutos. O corte é executado pelo roteador, sem depender do backend permanecer ligado. A persistência de contadores após reinício ou queda de energia deve ser testada no equipamento real.

O HotSpot e seu perfil precisam existir no roteador. A conta REST precisa das permissões para consultar, criar usuários, executar login, desativar usuários e remover sessões. O perfil deve permitir um usuário simultâneo e não impor outros limites incompatíveis com o plano. A autenticação dos clientes usa as contas locais criadas pelo portal.

O backend precisa alcançar o roteador e receber o IPv4 real do cliente. Uma hospedagem externa atrás de NAT/proxy não satisfaz automaticamente essa condição. O código não confia em MAC/IP enviado por query string nem em `X-Forwarded-For` arbitrário. O walled garden deve permitir o portal e os serviços efetivamente usados no checkout; validar também o acesso ao aplicativo bancário e impedir caminhos de saída que contornem o HotSpot.

Aplicar `scriptsDB/006_network_orders.sql` no banco escolhido, após os scripts anteriores, se as tabelas ainda não existirem. A migração não é automática. Os registros históricos de `access_sessions` são preservados. As chaves padrão do ASP.NET Data Protection precisam sobreviver aos reinícios para recuperar senhas de compras e cookies; em múltiplas instâncias é necessário compartilhar o key ring com proteção adequada.

Desativar `Network:Enabled` interrompe novos processamentos, mas não revoga sessões existentes; seus limites permanecem no roteador. Estornos dependem da entrega de notificações e da conciliação. Não há varredura completa de todos os pagamentos já finalizados.

## Verificação

```powershell
dotnet build asterlinkportaldepagamento.csproj --no-restore
dotnet run --project tests/NetworkIntegration/Network.Tests.csproj --no-restore
```

Os testes executam o cliente REST e a conciliação reais com transporte HTTP e persistência simulados. Cobrem autenticação, validação do pagamento, criação, conexão automática, IP alterado, dispositivo ausente, falhas de resposta, duplicidade, esgotamento e revogação. Não comprovam conectividade física, configuração do HotSpot, transações MySQL nem pagamentos reais. Não foi possível executar o ensaio físico porque o roteador não respondeu.

Não houve alteração de configuração no equipamento. Para concluir a validação operacional, conferir os valores reais do HotSpot, preencher os dados pendentes de pagamento e testar uma compra de curta duração, reconexão e corte por tempo no dispositivo.

## Referências oficiais consultadas antes da implementação

- [REST API: Basic Auth, métodos HTTP, JSON e IDs de registros](https://manual.mikrotik.com/docs/developer-guides/rest-api/).
- [Comando HotSpot active/login e seus argumentos](https://manual.mikrotik.com/docs/cli-reference/ip/hotspot/active/login/).
- [HotSpot: limit-uptime, MAC, usuários, perfis e sessões](https://help.mikrotik.com/docs/spaces/ROS/pages/56459266/HotSpot%2B-%2BCaptive%2Bportal).
- [Campos do usuário HotSpot](https://manual.mikrotik.com/docs/cli-reference/ip/hotspot/user/).
