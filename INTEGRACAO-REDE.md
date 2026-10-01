# Integração com a REST API oficial do RouterOS 7

O portal usa a REST API para identificar o cliente pelo **IPv4 de origem e gateway**, criar a conta HotSpot, conectar o cliente e revogar o acesso. A aplicação não captura, armazena, compara nem envia endereço MAC. As consultas selecionam explicitamente os campos necessários. O próprio RouterOS continua administrando sua rede normalmente; esta alteração não remove seus mecanismos internos de camada 2.

As credenciais administrativas e a senha aleatória de cada compra permanecem no backend. O navegador não recebe essas senhas nem precisa enviar um formulário de login ao HotSpot. A assinatura criptográfica HMAC do Mercado Pago permanece intacta: não é um endereço de dispositivo.

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

Esta alteração não muda `appsettings.Development.json`. Os valores locais de servidor, perfil, sub-rede e gateway precisam corresponder à instalação real. A rede dos clientes não pode ser deduzida do IP de gerenciamento do roteador. Nenhuma consulta ao equipamento foi feita para validar esta refatoração.

Mantida a URL HTTP já utilizada no desenvolvimento. HTTP transmite as credenciais sem criptografia: utilizar somente em uma rede de laboratório confiável. Para HTTPS, habilitar `www-ssl` com certificado confiável e alterar `RestUrl`; a aplicação não ignora certificados inválidos. REST usa `www`/`www-ssl`, e não as portas 8728/8729 da API binária.

`ConnectionStrings:DefaultConnection` e a seção `MercadoPago` continuam necessários para persistir pedidos e confirmar pagamentos. Removê-los impediria a liberação após pagamento. As opções `WebhookUrl`, `CollectorId` e `LiveMode` foram movidas de `Network` para `MercadoPago`, no mesmo arquivo.

Confira os dados reais de pagamento no ambiente utilizado:

- `MercadoPago:WebhookUrl`: URL HTTPS pública que recebe `/webhooks/mercadopago`.
- `MercadoPago:WebhookSecret`: segredo de assinatura configurado no Mercado Pago.
- `MercadoPago:CollectorId`: ID da conta recebedora correspondente ao token.

As credenciais de pagamento existentes foram preservadas. A aplicação recusa novas cobranças se a configuração obrigatória estiver incompleta. Em outro ambiente, forneça as mesmas seções por configuração apropriada ao ambiente; o arquivo Development não é carregado automaticamente em Production.

## Fluxo após o pagamento

1. O dispositivo abre `/rede/entrada`. O backend valida o IPv4 remoto em `ClientSubnet` e exige exatamente uma entrada com esse `address` e o servidor configurado na tabela `/ip/hotspot/host`. O cookie protegido contém apenas `Address`, `Gateway` e `Expires`.
2. O checkout revalida o IPv4 e gateway antes de pagar. Se diferirem do cookie ou do pedido existente, a operação é recusada. O preço e a duração vêm do plano no banco, não do navegador.
3. A resposta do pagamento e as notificações assinadas alimentam uma fila persistente. A conciliação consulta o pagamento no Mercado Pago e confere ID, referência, valor, moeda, recebedor e ambiente.
4. Somente `approved` cria um usuário via `PUT /rest/ip/hotspot/user`, com senha exclusiva da compra, servidor, perfil, `limit-uptime` e comentário que vincula o pedido ao IPv4. Um plano de 90 minutos envia `90m`. O campo `address` do usuário HotSpot não é usado como trava de origem: segundo a documentação, ele atribui endereço por tradução, mas não restringe o IP de login.
5. O backend procura novamente o **mesmo IPv4 do pedido** e executa `POST /rest/ip/hotspot/active/login` enviando apenas `ip`, `user` e `password`. Se o host tiver `to-address`, esse IPv4 traduzido é validado na sub-rede e usado no login. Depois consulta `/ip/hotspot/active` para confirmar usuário, IPv4 e servidor. Uma resposta HTTP de sucesso não basta.
6. Se o cliente estiver temporariamente ausente, a fila tenta novamente. A página também oferece uma tentativa manual pelo backend. Login e senha não são enviados ao navegador.
7. O RouterOS encerra o acesso quando o usuário atinge `limit-uptime`. Pagamentos estornados, contestados ou cancelados, confirmados pela conciliação, desativam o usuário com `PATCH` e removem sessões ativas com `DELETE`.

Notificações repetidas e respostas perdidas não recriam o usuário, não zeram contadores e não reiniciam uma sessão já ativa. A criação é persistida antes do login. Uma conta marcada como pronta que desapareça do roteador exige revisão, em vez de ganhar outra franquia automaticamente. Sessão existente deste pedido em outro endereço/servidor impede novo login automático.

## Transição e teste local

- Cookies anteriores são invalidados pelo novo propósito de proteção `AsterLink.Device.IPv4.v2`. Abra `/rede/entrada` novamente **no cliente conectado à rede de visitantes**, antes de acessar o checkout. Parar e iniciar o servidor não limpa os cookies do navegador.
- Nenhum registro do banco ou usuário do roteador é apagado ou convertido automaticamente. Os documentos antigos no banco continuam legíveis; campos antigos desconhecidos são ignorados e não são gravados em novos pedidos. Não foi executada limpeza dos dados históricos.
- Usuários já provisionados pelo modelo anterior não têm o novo marcador de IPv4 no comentário. A aplicação recusa sua adoção automática e exige revisão, preservando saldo/contadores. Para validar o novo fluxo em laboratório, use um **novo pedido de teste**; não renomeie comentários de contas antigas para contornar essa verificação.
- Confirme primeiro no terminal do MikroTik que `/ip hotspot host print detail` contém o IPv4 do cliente. Se a tabela estiver vazia, a entrada continua bloqueada e nenhum cookie é emitido. Remover MAC da aplicação não cria um host no roteador.
- No laboratório, `192.168.56.1` é o endereço do Windows que hospeda o portal. Abrir esse endereço no próprio Windows não garante passagem pelo CHR. Uma VM cliente na mesma rede de `ether2`, usando o CHR como gateway e gerando tráfego por ele, permite testar a identificação. Abra o portal **nessa VM cliente**.
- Após a aprovação, confira `/ip hotspot user print detail where name~"aster-"` e `/ip hotspot active print detail where user~"aster-"`. A sessão deve mostrar o IPv4 esperado e o tempo restante. Essas consultas podem mostrar informações internas do RouterOS que a aplicação não utiliza.

## Duração e requisitos da rede

A duração representa **minutos acumulados de conexão**, como já informado na tela do plano. Não é validade corrida desde a confirmação do pagamento. Desconectar preserva o saldo restante; a compra expirada não ganha novos minutos. O corte é executado pelo roteador, sem depender do backend permanecer ligado. A persistência de contadores após reinício ou queda de energia deve ser testada no equipamento real.

O HotSpot e seu perfil precisam existir no roteador. A conta REST precisa das permissões para consultar, criar usuários, executar login, desativar usuários e remover sessões. O perfil deve permitir um usuário simultâneo e não impor outros limites incompatíveis com o plano. A autenticação dos clientes usa as contas locais criadas pelo portal.

Esta configuração admite uma única sub-rede de clientes por gateway. O IPv4 de origem (`address`) e o endereço atribuído pelo HotSpot (`to-address`, quando presente) precisam pertencer a `Network:ClientSubnet`. Instalações com pool traduzido fora dessa faixa exigem ajuste de projeto/configuração; não são liberadas automaticamente.

O backend precisa alcançar o roteador e receber o IPv4 real do cliente. Uma hospedagem externa atrás de NAT/proxy não satisfaz automaticamente essa condição. O código não confia em IP enviado por query string nem em `X-Forwarded-For` arbitrário. O walled garden deve permitir o portal e os serviços efetivamente usados no checkout; validar também o acesso ao aplicativo bancário e impedir caminhos de saída que contornem o HotSpot.

**IP não é identidade permanente de aparelho.** Se o IP mudar, a aplicação não transfere o pedido para outro IP. Se o DHCP reaproveitar o mesmo IP para outro aparelho, a aplicação não consegue distingui-lo pelo IP sozinho. O especialista de redes precisa garantir unicidade, impedir falsificação de origem e evitar a reutilização do endereço enquanto houver acesso/saldo associado ao pedido, incluindo períodos desconectados. A implantação não está pronta para produção apenas por remover a dependência de endereço físico.

IPv6 nativo é recusado. Um IPv4 representado pelo ASP.NET como `::ffff:192.168.56.10` é normalizado para `192.168.56.10`; isso não habilita IPv6 no HotSpot. O especialista deve impedir que IPv6 permita contornar a cobrança na rede de visitantes.

Aplicar `scriptsDB/006_network_orders.sql` no banco escolhido, após os scripts anteriores, se as tabelas ainda não existirem. A migração não é automática. Os registros históricos de `access_sessions` são preservados. As chaves padrão do ASP.NET Data Protection precisam sobreviver aos reinícios para recuperar senhas de compras e cookies; em múltiplas instâncias é necessário compartilhar o key ring com proteção adequada.

Desativar `Network:Enabled` interrompe novos processamentos, mas não revoga sessões existentes; seus limites permanecem no roteador. Estornos dependem da entrega de notificações e da conciliação. Não há varredura completa de todos os pagamentos já finalizados.

## Verificação

```powershell
dotnet build asterlinkportaldepagamento.csproj --no-restore
dotnet run --project tests/NetworkIntegration/Network.Tests.csproj --no-restore
```

Os testes executam os controllers, cliente REST e conciliação com transporte HTTP e persistência simulados. Cobrem captura sem endereço físico, normalização IPv4, recusa de IPv6, cookie antigo/expirado/adulterado, mudança de IP, host ausente/ambíguo, tradução de IP, autenticação, validação do pagamento, criação, conexão automática, falhas de resposta, duplicidade, esgotamento e revogação. Não comprovam conectividade física, configuração do HotSpot, transações MySQL nem pagamentos reais. A refatoração não executou um ensaio de compra/liberação no CHR real.

Não houve alteração de configuração no equipamento. Para concluir a validação operacional, conferir os valores reais do HotSpot, preencher os dados pendentes de pagamento e testar uma compra de curta duração, reconexão e corte por tempo no dispositivo.

## Referências oficiais consultadas antes da implementação

- [REST API: Basic Auth, métodos HTTP, JSON e IDs de registros](https://manual.mikrotik.com/docs/developer-guides/rest-api/).
- [Comando HotSpot active/login e seus argumentos](https://manual.mikrotik.com/docs/cli-reference/ip/hotspot/active/login/).
- [HotSpot: suporte IPv4, limit-uptime, usuários, perfis e sessões](https://manual.mikrotik.com/docs/authentication-authorization-accounting/hotspot-captive-portal/).
- [Campos do usuário HotSpot](https://manual.mikrotik.com/docs/cli-reference/ip/hotspot/user/).
